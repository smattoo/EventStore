﻿using EventStore.ClientAPI;
using EventStore.Common.Utils;
using EventStore.Projections.Core.Services.Processing;
using NUnit.Framework;
using System;
using System.Threading;

namespace EventStore.Projections.Core.Tests.Services.emitted_stream_manager.when_deleting
{
    [TestFixture]
    public class with_an_existing_emitted_streams_stream : Base
    {
        protected Action _onDeleteStreamCompleted;
        protected ManualResetEvent _resetEvent = new ManualResetEvent(false);
        private string _testStreamName = "test_stream";

        protected override void Given()
        {
            _onDeleteStreamCompleted = () =>
            {
                _resetEvent.Set();
            };

            base.Given();

            _emittedStreamManager.TrackEmittedStream(new EmittedEvent[]
            {
                new EmittedDataEvent(
                    _testStreamName, Guid.NewGuid(),  "type1",  true,
                    "data", null, CheckpointTag.FromPosition(0, 100, 50), null),
            });

            var emittedStreamResult = _conn.ReadStreamEventsForwardAsync(_projectionNamesBuilder.GetEmittedStreamsName(), 0, 1, false, new EventStore.ClientAPI.SystemData.UserCredentials("admin", "changeit")).Result;
            Assert.AreEqual(1, emittedStreamResult.Events.Length);
            Assert.AreEqual(SliceReadStatus.Success, emittedStreamResult.Status);
        }

        protected override void When()
        {
            _emittedStreamManager.DeleteEmittedStreams(_onDeleteStreamCompleted);
            if (!_resetEvent.WaitOne(TimeSpan.FromSeconds(10)))
            {
                throw new Exception("Timed out waiting callback.");
            };
        }

        [Test]
        public void should_have_deleted_the_tracked_emitted_stream()
        {
            var result = _conn.ReadStreamEventsForwardAsync(_testStreamName, 0, 1, false, new EventStore.ClientAPI.SystemData.UserCredentials("admin", "changeit")).Result;
            Assert.AreEqual(SliceReadStatus.StreamNotFound, result.Status);
        }


        [Test]
        public void should_have_deleted_the_checkpoint_stream()
        {
            var result = _conn.ReadStreamEventsForwardAsync(_projectionNamesBuilder.GetEmittedStreamsCheckpointName(), 0, 1, false, new EventStore.ClientAPI.SystemData.UserCredentials("admin", "changeit")).Result;
            Assert.AreEqual(SliceReadStatus.StreamNotFound, result.Status);
        }

        [Test]
        public void should_have_deleted_the_emitted_streams_stream()
        {
            var result = _conn.ReadStreamEventsForwardAsync(_projectionNamesBuilder.GetEmittedStreamsName(), 0, 1, false, new EventStore.ClientAPI.SystemData.UserCredentials("admin", "changeit")).Result;
            Assert.AreEqual(SliceReadStatus.StreamNotFound, result.Status);
        }
    }
}
