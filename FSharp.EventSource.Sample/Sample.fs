module internal Option =
  let as_option = function
    | null -> None
    | x    -> Some x

  let (<!>) a b =
    match a with
    | None -> b
    | Some x -> Some x

  let (<.>) a b =
    match a with
    | None -> b
    | Some x -> x

module internal Sugar =
  /// Sugar for creating a tuple in a readable way
  let (=>) a b = (a, b)

  /// Maybe convert to int32 from string
  let muint32 str =
    match System.UInt32.TryParse str with
    | true, i -> Some i
    | _       -> None

// Example from https://github.com/Yaffle/EventSource
// Copyright Henrik Feldt 2013

open System
open System.IO
open System.Net
open System.Text

open FSharp
open Sugar
open Option
open Streams
open FSharp.EventSource

let known_types = [".js" => "text/javascript"; ".html" => "text/html"] |> Map.ofList
let header (key : string) (req : HttpListenerRequest) = req.Headers.[key] |> as_option
let query_str (key : string) (req : HttpListenerRequest) = req.QueryString.[key] |> as_option

let serve_file file (ctx : HttpListenerContext) =
  let resp = ctx.Response
  async {
    let file = if file = "/" then "index.html" else file.TrimStart('/')
    let typ  = known_types.TryFind(IO.Path.GetExtension(file)) |> fold (fun s t -> t) "text/plain"
    System.Console.WriteLine( sprintf "serving file %s" file )

    resp.AddHeader("Connection", "Close")
    resp.SendChunked       <- false
    resp.ContentType       <- sprintf "%s; charset=utf-8" typ
    resp.StatusCode        <- 200
    resp.StatusDescription <- "OK"

    // NOTE: obviously this is a security hole as it will serve ANY file, don't use IRL
    use fs = new FileStream(file, FileMode.Open)
    resp.ContentLength64  <- fs.Length
    do! resp.OutputStream <<! fs
    do! resp.OutputStream.FlushAsync()
      
    return resp.Close() }

let counter_demo (ctx : HttpListenerContext) =
  let req  = ctx.Request
  let resp = ctx.Response
  let out  = ctx.Response.OutputStream

  let write i =
    async {
      let msg = Message.Create(id = i, data = string i)
      do! msg |> send out
      do! Async.Sleep 100 }

  async {
    let last_evt_id =
      (req |> header "last-event-id" |> bind muint32) <!>
      (req |> query_str "lastEventId" |> bind muint32) <.>
      100u

    let actions =
      Seq.unfold
        (fun i -> if i = 0u then None else Some(write (string i), i-1u))
        (last_evt_id - 1u)

    for a in actions do
      do! a }

let handle_errors f x =
  async { try do! f x with e -> Console.WriteLine(e.ToString()) }

let route (ctx : HttpListenerContext) =
  let p  = ctx.Request.Url.AbsolutePath
  if p = "/events" then
    hand_shake counter_demo ctx
  else 
    serve_file p ctx

/// Create a new async server
let asyncServer listen = HttpListener.Start(listen, handle_errors route)

[<EntryPoint>]
let main argv =
  let server = asyncServer "http://*:8080/"
  Console.WriteLine("Press return to exit...")
  Console.ReadLine() |> ignore
  server.Cancel()
  0
