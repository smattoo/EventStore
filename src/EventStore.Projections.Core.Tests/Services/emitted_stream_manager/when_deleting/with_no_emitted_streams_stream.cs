﻿using NUnit.Framework;
using System;
using System.Threading;

namespace EventStore.Projections.Core.Tests.Services.emitted_stream_manager.when_deleting
{
    [TestFixture]
    public class with_no_emitted_streams_stream : Base
    {
        protected Action _onDeleteStreamCompleted;
        protected ManualResetEvent _resetEvent = new ManualResetEvent(false);

        protected override void Given()
        {
            _onDeleteStreamCompleted = () =>
            {
                _resetEvent.Set();
            };
            base.Given();
        }

        protected override void When()
        {
            _emittedStreamManager.DeleteEmittedStreams(_onDeleteStreamCompleted);
        }

        [Test]
        public void should_have_called_completed()
        {
            if (!_resetEvent.WaitOne(TimeSpan.FromSeconds(10)))
            {
                throw new Exception("Timed out waiting callback.");
            };
        }
    }
}
