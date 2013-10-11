# F# EventSource

Stream Updates with [Server-Sent Events](http://www.w3.org/TR/eventsource/).

Current status: gives you an API to write to the browser.

Use together with
[polyfille](https://github.com/remy/polyfills/blob/master/EventSource.js) for
browser support. Have a look at Sample.fs for an example on how to use it.

## License

MIT Copyright Henrik Feldt 2013

### Things to consider

 - I've added a dependency on FSharp.Actor because I will be adding support for
   writing a comment every 14 or 15 seconds to keep the stream from dying.
 - Adding proper content negotiation to the sample just so I don't send JS as
   text/html.
 - Finding and recommending F# web tools that can do the HTTP handling for you
   to make it quicker to work.
 - Making it a DLL instead of an executable
