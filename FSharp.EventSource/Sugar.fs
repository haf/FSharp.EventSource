namespace FSharp

open System
open System.Net
open System.Text

[<AutoOpen>]
module AsyncExtensions =
  open System
  open System.Threading
  open System.Threading.Tasks

  type Microsoft.FSharp.Control.Async with
    static member Raise (e : #exn) =
      Async.FromContinuations(fun (_,econt,_) -> econt e)

    /// Starts the specified operation using a new CancellationToken and returns
    /// IDisposable object that cancels the computation. This method can be used
    /// when implementing the Subscribe method of IObservable interface.
    static member StartDisposable(op:Async<unit>) =
      let ct = new System.Threading.CancellationTokenSource()
      Async.Start(op, ct.Token)
      { new IDisposable with
          member x.Dispose() = ct.Cancel() }

    /// Starts a Task<'a> with the timeout and cancellationToken and
    /// returns a Async<a' option> containing the result. If the Task does
    /// not complete in the timeout interval, or is faulted None is returned.
    static member TryAwaitTask(task:Tasks.Task<_>, ?timeout, ?cancellationToken) =
      let timeout = defaultArg timeout Timeout.Infinite
      let cancel = defaultArg cancellationToken Async.DefaultCancellationToken
      async {
        return
          if task.Wait(timeout, cancel) && not task.IsCanceled && not task.IsFaulted
          then Some task.Result
          else None }

    static member AwaitTask (t : Task) =
      let flattenExns (e : AggregateException) = e.Flatten().InnerExceptions |> Seq.nth 0
      let rewrapAsyncExn (it : Async<unit>) =
        async { try do! it with :? AggregateException as ae -> do! (Async.Raise <| flattenExns ae) }
      let tcs = new TaskCompletionSource<unit>(TaskCreationOptions.None)
      t.ContinueWith((fun t' ->
        if t.IsFaulted then tcs.SetException(t.Exception |> flattenExns)
        elif t.IsCanceled then tcs.SetCanceled ()
        else tcs.SetResult(())), TaskContinuationOptions.ExecuteSynchronously)
      |> ignore
      tcs.Task |> Async.AwaitTask |> rewrapAsyncExn

  /// Implements an extension method that overloads the standard
  /// 'Bind' of the 'async' builder. The new overload awaits on
  /// a standard .NET task
  type Microsoft.FSharp.Control.AsyncBuilder with
      member x.Bind(t:Task<'T>, f:'T -> Async<'R>) : Async<'R> = async.Bind(Async.AwaitTask t, f)
      member x.Bind(t:Task, f:unit -> Async<'R>) : Async<'R> = async.Bind(Async.AwaitTask t, f)


module internal Option =
  let as_option = function
    | null -> None
    | x    -> Some x

  let (<!>) a b =
    match a with
    | None -> a
    | Some x -> Some x

  let (<.>) a b =
    match a with
    | None -> b
    | Some x -> x

module internal Sugar =
  open Option
  open System
  open System.IO
  open System.Net
  open System.Threading
  open System.Collections.Generic

  /// Sugar for creating a tuple in a readable way
  let (=>) a b =
    (a, b)

  let muint32 str =
    match UInt32.TryParse str with
    | true, i -> Some i
    | _       -> None

  let header (key : string) (req : HttpListenerRequest) =
    req.Headers.[key] |> as_option

  let qs (key : string) (req : HttpListenerRequest) =
    req.QueryString.[key] |> as_option

  type System.Net.HttpListener with
    /// Asynchronously retrieves the next incoming request
    member x.AsyncGetContext() = 
      Async.FromBeginEnd(x.BeginGetContext, x.EndGetContext)

  type System.Net.HttpListener with 
    /// Starts an HTTP server on the specified URL with the
    /// specified asynchronous function for handling requests
    static member Start(url, f) = 
      let tokenSource = new CancellationTokenSource()
      let handle_errors f =
        async { try do! f with e -> printfn "%s" <| e.ToString() }
          
      Async.Start
        ( ( async { 
              use listener = new HttpListener()
              listener.Prefixes.Add(url)
              listener.Start()
              while true do 
                let! context = listener.AsyncGetContext()
                Async.Start(handle_errors(f context), tokenSource.Token) }),
          cancellationToken = tokenSource.Token)
      tokenSource

module Streams =

  open Sugar
  open Option
  open System.IO
  open System.Text

  /// Asynchronouslyo write from the 'from' stream to the 'to' stream.
  let (<<!) (toStream : Stream) (from : Stream) =
    let buf = Array.zeroCreate<byte> 0x2000
    let rec doBlock () =
      async {
        let! read = from.AsyncRead buf
        if read <= 0 then
          toStream.Flush()
          return ()
        else
          do! toStream.AsyncWrite(buf, 0, read)
          return! doBlock () }
    doBlock ()

  /// Asynchronously write from the string 'data', utf8 encoded to the 'stream'.
  let (<<.) (stream : Stream) (data : string) =
    async {
      printf "%s" data
      let data = Encoding.UTF8.GetBytes data
      use ms = new IO.MemoryStream(data)
      do! stream <<! ms }
