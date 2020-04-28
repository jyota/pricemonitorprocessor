// Learn more about F# at http://fsharp.org

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
  with Failure _ -> ("/" + currstr, hitstr)


let resultsHandler (html:string) =
    html
    |> createDoc
    |> fun n -> n.Descendants(0)
    |> Seq.filter (fun s -> s.Descendants(0).Count() = 0)
    |> Seq.filter (fun s -> (innerText s).Contains("19.45"))
    |> Seq.map (fun n -> 
        let resPath, targetItem = (buildPath (parent n) "" "" "pric") in
        resPath)
    |> Array.ofSeq


let resultsSelector (html:string) (path:string) =
    html
    |> createDoc
    |> fun n -> n.SelectNodes(path)
    |> fun n -> n.First()
    |> innerText


[<EntryPoint>]
let main argv =
    //let html = Http.RequestString("https://www.amazon.com/Shin-Megami-Tensei-Nocturne-playstation-2/dp/B00024W1U6") in
    //printfn "%s" html    
    let html = System.IO.File.ReadAllText "test_input.html" in
    let results = resultsHandler html in
    //printfn "%s" results.[0]
    printfn "%s" (resultsSelector html results.[0])
    0
