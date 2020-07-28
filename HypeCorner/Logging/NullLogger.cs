using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HypeCorner.Logging
{
	/// <summary>
	/// Ignores all log events
	/// </summary>
	public class NullLogger : ILogger
	{
		/// <summary>
		/// The level of logging to apply to this logger.
		/// </summary>
		public LogLevel Level { get; set; }

		/// <summary>
		/// Informative log messages
		/// </summary>
		/// <param name="message"></param>
		/// <param name="args"></param>
		public void Trace(string message, string application = "app", params object[] args)
		{
			//Null Logger, so no messages are acutally sent
		}

		/// <summary>
		/// Informative log messages
		/// </summary>
		/// <param name="message"></param>
		/// <param name="args"></param>
		public void Info(string message, string application = "app", params object[] args)
		{
			//Null Logger, so no messages are acutally sent
		}

		/// <summary>
		/// Warning log messages
		/// </summary>
		/// <param name="message"></param>
		/// <param name="args"></param>
		public void Warning(string message, string application = "app", params object[] args)
		{
			//Null Logger, so no messages are acutally sent 
		}

		/// <summary>
		/// Error log messsages
		/// </summary>
		/// <param name="message"></param>
		/// <param name="args"></param>
		public void Error(string message, string application = "app", params object[] args)
		{
			//Null Logger, so no messages are acutally sent
		}
	}
}