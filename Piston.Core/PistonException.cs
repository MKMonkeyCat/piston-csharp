using System;
using System.Net;

namespace Piston.Core
{
  public class PistonException : Exception
  {
    public HttpStatusCode? StatusCode { get; }
    public string ResponseContent { get; }

    public PistonException(string message) : base(message)
    {
    }

    public PistonException(string message, Exception inner) : base(message, inner)
    {
    }

    public PistonException(HttpStatusCode statusCode, string content)
        : base($"Request failed with status {(int)statusCode} ({statusCode}) - Response: {content}")
    {
      StatusCode = statusCode;
      ResponseContent = content;
    }
  }
}
