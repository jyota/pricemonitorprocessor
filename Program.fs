open System
open FSharp.Data
open System.Linq
open HtmlAgilityPack.FSharp


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


[<EntryPoint>]
let main argv =
    //let html = Http.RequestString("https://www.amazon.com/Shin-Megami-Tensei-Nocturne-playstation-2/dp/B00024W1U6") in
    //let html = Http.RequestString("https://www.walmart.com/ip/Hamilton-Beach-6-Speed-Hand-Mixer-with-Snap-On-Case-Black/21125971") in
    //let path, priceSubpath = resultsHandler html "14.95" in
    let html = System.IO.File.ReadAllText "test_input.html" in
    let path, priceSubpath = resultsHandler html "19.45" in
    printfn "%s" (resultsSelector html path)
    0
