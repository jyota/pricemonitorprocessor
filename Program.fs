open System
open FSharp.Data
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


let resultsHandler (html:string) (targetPrice:string)=
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
    printfn "%s" path
    html
    |> createDoc
    |> fun n -> n.SelectNodes(path)
    |> fun n -> n.First()
    |> innerText

(*
[<EntryPoint>]
let main argv =
    //let html = Http.RequestString("https://www.amazon.com/Shin-Megami-Tensei-Nocturne-playstation-2/dp/B00024W1U6") in
    let html = Http.RequestString("https://shop.lululemon.com/p/women-pants/Align-Pant-2/_/prod2020012?color=42629") in
    let path, priceSubpath = resultsHandler html "98.00" in
    //let html = System.IO.File.ReadAllText "test_input.html" in
    //let path, priceSubpath = resultsHandler html "19.45" in
    printfn "%s" (resultsSelector html path)
    0
*)

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

(*  if t.Length > 0
    then
      t
      |> List.map (fun r -> 
          use actionsCmd = PricingMonitorDb.CreateCommand<"
                                SELECT id, action_id, action_trigger_id, action_trigger_threshold,
                                       threshold_type_id, action_target_text
                                FROM intake.url_target_actions 
                                WHERE url_target_id = @url_target_id">(PricingMonitorDbConnectionString)
          let tActions = actionsCmd.Execute(url_target_id = r.id)
          r)
*)

let handleJob (jobData : MonitorRequest)= 
  async {
    printfn "\nURL: %s\nTarget: %f\nFirst alert target: %s\n" jobData.Url jobData.TargetPrice jobData.MonitorRequestActions.[0].ActionTargetText
  }

[<EntryPoint>]
let main argv =
    getAllJobs
    |> List.map handleJob
    |> Async.Parallel
    |> Async.Ignore
    |> Async.RunSynchronously
    0
