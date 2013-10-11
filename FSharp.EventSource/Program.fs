open System
open System.Net
open System.Text

open FSharp.EventSource
open Sugar
open Streams
open Option

// Example from https://github.com/Yaffle/EventSource
// Copyright Henrik Feldt 2013
let asyncServer () = HttpListener.Start("http://*:8080/", fun ctx ->
  let resp = ctx.Response
  let req  = ctx.Request

  let serve_file file (resp : HttpListenerResponse) =
    async {
      let file = if file = "/" then "index.html" else file.TrimStart('/')
      printfn "serving file %s" file

      let bytes = IO.File.ReadAllBytes file
      resp.AddHeader("Connection", "Close")
      resp.SendChunked       <- false
      resp.ContentType       <- "text/html; charset=utf-8"
      resp.ContentLength64   <- bytes.LongLength
      resp.StatusCode        <- 200
      resp.StatusDescription <- "OK"

      // obviously this is a security hole as it will serve ANY file, don't use IRL
      use fs = new IO.FileStream(file, IO.FileMode.Open)
      do! resp.OutputStream <<! fs
      do! resp.OutputStream.FlushAsync()
      
      return resp.Close() }

  let rec event_source_protocol () =
    write_headers <| resp.OutputStream

  and write_headers out =
    async {
      [ "Content-Type"                => "text/event-stream; charset=utf-8"
      ; "Cache-Control"               => "no-cache"
      ; "Access-Control-Allow-Origin" => "*" ]
      |> List.iter(fun (k, v) -> resp.AddHeader(k, v))
      resp.StatusDescription <- "OK"

      do! out <<. ":" + new String(' ', 2048) + "\n" // 2kB padding for IE
      do! out <<. "retry: 2000\n"

      return! write_loop out }

  and write_loop out =

    let write i =
      async {
        do! out <<. "id: " + i + "\n"
        do! out <<. "data: " + i + ";\n\n"
        do! out.FlushAsync()
        do! Async.Sleep(1000) }

    async {
      let last_evt_id =
        (req |> header "last-event-id" |> bind muint32) <!>
        (req |> qs "lastEventId" |> bind muint32) <.>
        0u;

      let actions =
        Seq.unfold
          (fun i -> if i = last_evt_id then None else Some(write (string i), i-1u))
          (last_evt_id + 100u)

      for a in actions do
        do! a

      return () }

  if req.Url.AbsolutePath = "/events" then event_source_protocol () else serve_file req.Url.AbsolutePath resp)

[<EntryPoint>]
let main argv =
  let server = asyncServer ()
  printfn "Press return to exit..."
  Console.ReadLine() |> ignore
  server.Cancel()
  0