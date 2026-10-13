using System;
using System.Text.Json;

namespace Replier
{
    /// <summary>Thrown when the API answers with "success": false, or when the HTTP call fails.</summary>
    public class ReplierException : Exception
    {
        /// <summary>API error code, e.g. "0054".</summary>
        public string? ErrorCode { get; }

        /// <summary>Decoded response body, when there was one.</summary>
        public JsonElement? Response { get; }

        public int HttpStatus { get; }

        public ReplierException(string message, string? errorCode = null, JsonElement? response = null,
            int httpStatus = 0, Exception? inner = null) : base(message, inner)
        {
            ErrorCode = errorCode;
            Response = response;
            HttpStatus = httpStatus;
        }
    }
}
