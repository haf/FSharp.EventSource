namespace FSharp

/// See www.w3.org/TR/eventsource/#event-stream-interpretation
module EventSource =
  // welcome to a stringly typed API

  open FSharp
  open Sugar
  open Streams
  open System.IO

  let [<Literal>] private EOL = "\n"

  /// "If the line is empty (a blank line) - dispatch the event."
  /// Dispatches the event properly to the browser.
  let dispatch (out : Stream) =
    async {
      do! out <<. EOL
      do! out.FlushAsync() }

  /// "If the line starts with a U+003A COLON character (:) - Ignore the line."
  /// Writes a comment to the stream
  let comment (out : Stream) (cmt : string) =
    async { do! out <<. ": " + cmt + EOL }

  /// "If the field name is 'event' - Set the event type buffer to field value."
  /// Writes the event type to the stream
  let event_type (out : Stream) (event_type : string) =
    async { do! out <<. "event: " + event_type + EOL }

  /// "If the field name is 'data' -
  /// Append the field value to the data buffer, then append a single
  /// U+000A LINE FEED (LF) character to the data buffer."
  /// Write a piece of data as part of the event
  let data (out : Stream) (data : string) =
    async { do! out <<. "data: " + data + EOL }

  /// "If the field name is 'id' - Set the last event ID buffer to the field value."
  /// Sets the last event id in the stream.
  let id (out : Stream) (last_event_id : string) =
    async { do! out <<. "id: " + last_event_id + EOL }

  /// "If the field name is 'retry' - If the field value consists of only ASCII digits, then interpret the field value as an integer in base ten, and set the event stream's reconnection time to that integer. Otherwise, ignore the field."
  /// Writes a control line to the EventSource listener, that changes
  /// how quickly reconnects happen. A reconnection time, in milliseconds.
  /// This must initially be a user-agent-defined value, probably in the region of
  /// a few seconds.
  let retry (out : Stream) (retry : uint32) =
    async { do! out <<. "retry: " + (string retry) + EOL }

  /// A container data type for the output events
  type Message =
    { id       : string
    ; data     : string
    ; ``type`` : string option }
    static member Create(id, data, ?typ) =
      let typ = defaultArg typ None
      { id = id; data = data; ``type`` = typ }

  /// send a message containing data to the output stream
  let send (out : Stream) (msg : Message) =
    let lift_opt f = function | Some x -> async { do! f x } | _ -> async { return () }
    async {
      do! msg.id |> id out
      do! msg.``type`` |> lift_opt (event_type out)
      do! msg.data |> data out
      return! dispatch out }