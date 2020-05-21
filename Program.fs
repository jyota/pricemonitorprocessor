open System
//open FSharp.Data
open System.Linq
open HtmlAgilityPack.FSharp
open FSharp.Data.Npgsql
open System.IO
open PriceMonitorProcessor.Models 


// Database
[<Literal>]
let PricingMonitorDbConnectionString = "Host=127.0.0.1;Port=5433;Username=sa;Password=data1;Database=pricing_monitor_db"
type PricingMonitorDb = NpgsqlConnection<PricingMonitorDbConnectionString>


let rec buildPath (el:HtmlAgilityPack.HtmlNode) (currstr:string) (hitstr:string) (findstr:string) = 
  try
    if (isNull el) || (el.Name = "html") then ("/" + currstr, hitstr) else
        let thisParent = parent el in 
        let path = ("/" + el.Name + currstr) in
        if hitstr = "" then 
          if ((attr "class" el) <> "") && (attr "class" el).Contains(findstr)
          then buildPath thisParent path path findstr
          else buildPath thisParent path "" findstr
        else 
          buildPath thisParent path hitstr findstr
  with
  | :? System.NullReferenceException -> ("/" + currstr, hitstr)
  | _ -> reraise()


let valueTargetHandler (html:string) (targetPrice:string)=
    html
    |> createDoc
    |> descendants 0
    |> Seq.filter (fun s -> not s.HasChildNodes)
    |> Seq.filter (fun s -> (innerText s).Contains(targetPrice))
    |> Seq.map (fun n -> 
        (buildPath (parent n) "" "" "pric"))
    |> Seq.filter (fun (path, pricePath) -> pricePath <> "")
    |> Seq.head

let resultsSelector (html:string) (path:string) =
    html
    |> createDoc
    |> fun n -> n.SelectNodes(path)
    |> fun n -> n.First()
    |> innerText


type ScrapedPathInfo = 
  {
    Path : string
    PriceSubPath : string
    FoundPrice : bool
  }

let scrapePriceURL (url:string) (targetPrice:string) =
  // let html = Http.RequestString(url)
  // Currently using local test file to avoid hitting remote servers too much
  let html = System.IO.File.ReadAllText "test_input.html"
  let path, priceSubPath = valueTargetHandler html targetPrice 
  let foundPrice = resultsSelector html path
  {Path = path; PriceSubPath = priceSubPath; FoundPrice = not (isNull foundPrice)}
   

let insertUrl (url : string) (conn : Npgsql.NpgsqlConnection) (tx : Npgsql.NpgsqlTransaction) =
    use cmd = new NpgsqlCommand<"
        INSERT INTO md.urls (url)
        VALUES (@url)
        RETURNING id", PricingMonitorDbConnectionString>(conn, tx)
    let t = cmd.Execute(url = url)
    t.Head

let insertMonitor (urlId : int64) (conn : Npgsql.NpgsqlConnection) (tx : Npgsql.NpgsqlTransaction) =
    use cmd = new NpgsqlCommand<"
        INSERT INTO md.monitors (url_id, enabled)
        VALUES (@url_id, true)
        RETURNING id", PricingMonitorDbConnectionString>(conn, tx)
    let t = cmd.Execute(url_id = urlId)
    t.Head

let insertDomPath (path : string) (conn : Npgsql.NpgsqlConnection) (tx : Npgsql.NpgsqlTransaction) =
    use cmd = new NpgsqlCommand<"
        INSERT INTO md.dom_paths (path)
        VALUES (@path)
        RETURNING id", PricingMonitorDbConnectionString>(conn, tx)
    let t = cmd.Execute(path = path)
    t.Head

let insertMonitorsDomPath (monitor_id : int64) (dom_path_id : int64) (monitor_dom_target_type_id : int64) (conn : Npgsql.NpgsqlConnection) (tx : Npgsql.NpgsqlTransaction) = 
    use cmd = new NpgsqlCommand<"
        INSERT INTO md.monitors_dom_paths (monitor_id, dom_path_id, monitor_dom_target_type_id)
        VALUES (@monitor_id, @dom_path_id, @monitor_dom_target_type_id)
        RETURNING id", PricingMonitorDbConnectionString>(conn, tx)
    let t = cmd.Execute(monitor_id = monitor_id, dom_path_id = dom_path_id, monitor_dom_target_type_id = monitor_dom_target_type_id)
    t.Head

let insertUserMonitor (monitor_id : int64) (user_id : int64) (conn : Npgsql.NpgsqlConnection) (tx : Npgsql.NpgsqlTransaction) =
    use cmd = new NpgsqlCommand<"
        INSERT INTO md.user_monitors (monitor_id, user_id)
        VALUES (@monitor_id, @user_id)
        RETURNING id", PricingMonitorDbConnectionString>(conn, tx)
    let t = cmd.Execute(monitor_id = monitor_id, user_id = user_id)
    t.Head

let insertPriceHistory (monitor_id : int64) (price : decimal) (conn : Npgsql.NpgsqlConnection) (tx : Npgsql.NpgsqlTransaction) =
    use cmd = new NpgsqlCommand<"
        INSERT INTO md.price_history (price, monitor_source_id)
        VALUES (@price, @monitor_id)
        RETURNING id", PricingMonitorDbConnectionString>(conn, tx)
    let t = cmd.Execute(price = price, monitor_id = monitor_id)
    t.Head

let clearIntakeData (monitor_request_id : int64) (conn : Npgsql.NpgsqlConnection) (tx : Npgsql.NpgsqlTransaction) =
    use cmd = new NpgsqlCommand<"
        DELETE FROM intake.url_target_actions WHERE url_target_id = @request_id;
        ", PricingMonitorDbConnectionString>(conn, tx)
    ignore <| cmd.Execute(request_id = monitor_request_id)
    use cmd = new NpgsqlCommand<"
        DELETE FROM intake.url_target WHERE id = @request_id;
        ", PricingMonitorDbConnectionString>(conn, tx)
    ignore <| cmd.Execute(request_id = monitor_request_id)

let getDomPathTypeId (name : string) (conn : Npgsql.NpgsqlConnection) (tx : Npgsql.NpgsqlTransaction) = 
  use cmd = new NpgsqlCommand<"
        SELECT id FROM md.target_types
        WHERE name = @name", PricingMonitorDbConnectionString>(conn, tx)
  let t = cmd.Execute(name = name)
  t.Head

let monitorIntake (monitorRequest : MonitorRequest) (path : string) (price : decimal) =
    use conn = new Npgsql.NpgsqlConnection(PricingMonitorDbConnectionString)
    conn.Open()
    use tx = conn.BeginTransaction()
    use cmd = new NpgsqlCommand<"
        SELECT id FROM md.urls WHERE url = @url", PricingMonitorDbConnectionString>(conn, tx)
    let t = cmd.Execute(url = monitorRequest.Url)
    let urlId = if t.Length > 0 then t.Head else insertUrl monitorRequest.Url conn tx

    use cmd = new NpgsqlCommand<"
        SELECT id FROM md.monitors WHERE url_id = @url_id", PricingMonitorDbConnectionString>(conn, tx)
    let mt = cmd.Execute(url_id = urlId)
    let monitorId = if mt.Length > 0 then mt.Head else insertMonitor urlId conn tx

    // TODO: ensure an old/expired DOM path gets replaced with the newer one from intake if URL is the same(?)
    use cmd = new NpgsqlCommand<"
        SELECT id FROM md.dom_paths WHERE path = @path", PricingMonitorDbConnectionString>(conn, tx)
    let t = cmd.Execute(path = path)
    let domPathId = if t.Length > 0 then t.Head else insertDomPath path conn tx
    let priceDomPathTypeId = getDomPathTypeId "Price" conn tx

    use cmd = new NpgsqlCommand<"
        SELECT id FROM md.monitors_dom_paths 
        WHERE monitor_id = @monitor_id 
        AND dom_path_id = @dom_path_id
        AND monitor_dom_target_type_id = @target_type_id", PricingMonitorDbConnectionString>(conn, tx)
    let t = cmd.Execute(monitor_id = monitorId, dom_path_id = domPathId, target_type_id = priceDomPathTypeId)
    ignore <| if t.Length = 0 then insertMonitorsDomPath monitorId domPathId priceDomPathTypeId conn tx else int64 0

    use cmd = new NpgsqlCommand<"
        SELECT id FROM md.user_monitors 
        WHERE monitor_id = @monitor_id
        AND user_id = @user_id", PricingMonitorDbConnectionString>(conn, tx)
    let t = cmd.Execute(monitor_id = monitorId, user_id = monitorRequest.RequestingUserId)
    ignore <| if t.Length > 0 then t.Head else insertUserMonitor monitorId monitorRequest.RequestingUserId conn tx

    // If monitor was not here before, insert first price into it's history
    ignore <| if mt.Length = 0 then insertPriceHistory monitorId price conn tx else int64 0
    clearIntakeData monitorRequest.Id conn tx
    tx.Commit()
    true

let getMonitorRequestActions urlTargetId = 
  use cmd = PricingMonitorDb.CreateCommand<"
                  SELECT id, action_id, action_trigger_id, action_trigger_threshold, threshold_type_id, action_target_text
                  FROM intake.url_target_actions
                  WHERE url_target_id = @url_target_id">(PricingMonitorDbConnectionString)
  let t = cmd.Execute(url_target_id = urlTargetId)
  t |> List.map (fun r -> {Id = r.id; ActionId = r.action_id; ActionTriggerId = r.action_trigger_id; 
                          ActionTriggerThreshold = r.action_trigger_threshold; ThresholdTypeId = r.threshold_type_id;
                          ActionTargetText = r.action_target_text})
    |> Array.ofSeq

let getAllJobs = 
  use cmd = PricingMonitorDb.CreateCommand<"
                  SELECT id, url, target_price, requesting_user_id
                  FROM intake.url_target">(PricingMonitorDbConnectionString)
  let t = cmd.Execute()
  t |> List.map (fun r -> {Id = r.id; Url = r.url; TargetPrice = r.target_price; RequestingUserId = r.requesting_user_id;
                          MonitorRequestActions = (getMonitorRequestActions r.id)})


let handleJob (jobData : MonitorRequest) = 
  async {
    let scrapedInfo = scrapePriceURL jobData.Url (string jobData.TargetPrice)
    if scrapedInfo.FoundPrice then
      if monitorIntake jobData scrapedInfo.Path jobData.TargetPrice then 
        printfn "\nURL: %s\nTarget: %f\nPath: %s\nFound price %b\n" jobData.Url jobData.TargetPrice scrapedInfo.Path scrapedInfo.FoundPrice
      else
        printfn "\nUnable to intake monitor\n"
    else
      printfn "\nDidn't find price\n"
  }

let scrapeForPriceAtURL (url:string) (path:string) =
  // let html = Http.RequestString(url)
  // Currently using local test file to avoid hitting remote servers too much
  let html = System.IO.File.ReadAllText "test_input.html"
  let priceString = resultsSelector html path
  decimal (Seq.fold (fun (str: string) chr -> str.Replace(chr, ' ')) priceString "$")

let handleMonitorScrape (monitor : MonitorBase) = 
  async {
    let scrapedPrice = scrapeForPriceAtURL monitor.Url monitor.Path
    use conn = new Npgsql.NpgsqlConnection(PricingMonitorDbConnectionString)
    conn.Open()
    use tx = conn.BeginTransaction()
    ignore <| insertPriceHistory monitor.Id scrapedPrice conn tx
    tx.Commit()
  }

let getAllMonitors = 
  use cmd = PricingMonitorDb.CreateCommand<"
                SELECT 
                    m.id,
                    u.url,
                    dp.path
                FROM md.monitors m
                JOIN md.urls u ON m.url_id = m.id
                JOIN md.monitors_dom_paths mdp ON mdp.monitor_id = m.id 
                JOIN md.target_types tt ON tt.id = mdp.dom_path_id 
                JOIN md.dom_paths dp ON dp.id = mdp.dom_path_id
                WHERE m.enabled
                AND tt.name = 'Price';">(PricingMonitorDbConnectionString)
  let t = cmd.Execute()
  t |> List.map (fun r -> {Id = r.id; Url = r.url; Path = r.path})


[<EntryPoint>]
let main argv =
    if argv.Length > 0 then
        match argv.[0] with 
         | "--intake" -> 
                getAllJobs
                |> List.map handleJob
                |> Async.Parallel
                |> Async.Ignore
                |> Async.RunSynchronously
                0
         | "--scrape" ->
                getAllMonitors
                |> List.map handleMonitorScrape
                |> Async.Parallel
                |> Async.Ignore 
                |> Async.RunSynchronously
                0
         | _ -> 
            printfn "\nPlease pass --intake or --scrape argument.\n"
            0
    else 
    printfn "\nPlease pass --intake or --scrape argument.\n"
    0
