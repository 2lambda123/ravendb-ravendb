//-----------------------------------------------------------------------
// <copyright file="ConcurrencyException.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using System;
using Raven.Abstractions.Data;

namespace Raven.Abstractions.Exceptions
{
    /// <summary>
    /// This exception is raised when a concurrency conflict is encountered
    /// </summary>
    public class ConcurrencyException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ConcurrencyException"/> class.
        /// </summary>
        public ConcurrencyException()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ConcurrencyException"/> class.
        /// </summary>
        /// <param name="message">The message.</param>
        public ConcurrencyException(string message) : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ConcurrencyException"/> class.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="inner">The inner.</param>
        public ConcurrencyException(string message, Exception inner) : base(message, inner)
        {
        }

        /// <summary>
        /// Expected Etag.
        /// </summary>
        public Etag ExpectedETag { get; set; }

        /// <summary>
        /// Actual Etag.
        /// </summary>
        public Etag ActualETag { get; set; }
    }
}
