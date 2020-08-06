using System;
using System.Collections.Generic;
using System.Text;

namespace HypeCorner.Exceptions
{
    /// <summary>
    /// The exception that is thrown when the watched stream no longer qualifies for being watched.
    /// </summary>
    public class WatchException : Exception
    {
        public WatchException(string essage) : base(essage) { }
    }
}
