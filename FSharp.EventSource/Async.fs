namespace FSharp

[<AutoOpen>]
module AsyncExtensions =
  open System
  open System.Threading
  open System.Threading.Tasks
  open System.Net

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

[<AutoOpen>]
module Streams =
  open Option
  open System.IO
  open System.Text

  /// Asynchronouslyo write from the 'from' stream to the 'to' stream.
  let transfer (to_stream : Stream) (from : Stream) =
    let buf = Array.zeroCreate<byte> 0x2000
    let rec doBlock () =
      async {
        let! read = from.AsyncRead buf
        if read <= 0 then
          to_stream.Flush()
          return ()
        else
          do! to_stream.AsyncWrite(buf, 0, read)
          return! doBlock () }
    doBlock ()

  /// Asynchronouslyo write from the 'from' stream to the 'to' stream.
  let (<<!) to_stream from =
    transfer to_stream from

  /// Asynchronously write from the string 'data', utf8 encoded to the 'stream'.
  let print_utf8 (stream : Stream) (data : string) =
    async {
      let data = Encoding.UTF8.GetBytes data
      use ms = new MemoryStream(data)
      do! stream <<! ms }

  /// Asynchronously write from the string 'data', utf8 encoded to the 'stream'.
  let (<<.) stream data =
    print_utf8 stream data