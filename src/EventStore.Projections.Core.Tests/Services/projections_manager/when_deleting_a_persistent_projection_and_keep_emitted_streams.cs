using System;
using System.Collections.Generic;
using System.Linq;
using EventStore.Common.Utils;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services;
using NUnit.Framework;
using EventStore.Core.Data;

namespace EventStore.Projections.Core.Tests.Services.projections_manager
{
    [TestFixture]
    public class when_deleting_a_persistent_projection_and_keep_emitted_streams : TestFixtureWithProjectionCoreAndManagementServices
    {
        protected string _projectionName;
        protected string _projectionSource;
        protected Type _fakeProjectionType;
        protected ProjectionMode _projectionMode;
        protected bool _checkpointsEnabled;
        protected bool _emitEnabled;

        protected override void Given()
        {
            base.Given();
            AllWritesSucceed();
            NoOtherStreams();

            _projectionName = "test-projection";
            _projectionSource = @"";
            _fakeProjectionType = typeof(FakeBiStateProjection);
            _projectionMode = ProjectionMode.Continuous;
            _checkpointsEnabled = true;
            _emitEnabled = false;
        }

        protected override IEnumerable<WhenStep> When()
        {
            yield return (new SystemMessage.BecomeMaster(Guid.NewGuid()));
            yield return (new SystemMessage.SystemReady());
            yield return
                (new ProjectionManagementMessage.Command.Post(
                    new PublishEnvelope(_bus), _projectionMode, _projectionName,
                    ProjectionManagementMessage.RunAs.System, "native:" + _fakeProjectionType.AssemblyQualifiedName,
                    _projectionSource, enabled: true, checkpointsEnabled: _checkpointsEnabled,
                    emitEnabled: _emitEnabled));

            Guid _reader;
            var readerAssignedMessage =
                _consumer.HandledMessages.OfType<EventReaderSubscriptionMessage.ReaderAssignedReader>().LastOrDefault();
            Assert.IsNotNull(readerAssignedMessage);
            _reader = readerAssignedMessage.ReaderId;

            yield return
                (ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
                    _reader, new TFPos(100, 50), new TFPos(100, 50), "stream1", 1, "stream1", 1, false, Guid.NewGuid(),
                    "type", false, Helper.UTF8NoBom.GetBytes("1"), new byte[0], 100, 33.3f));

            yield return
                new ProjectionManagementMessage.Command.Disable(
                    new PublishEnvelope(_bus), _projectionName, ProjectionManagementMessage.RunAs.System);

            yield return
                new ProjectionManagementMessage.Command.Delete(
                    new PublishEnvelope(_bus), _projectionName, ProjectionManagementMessage.RunAs.System, false, false, false);
        }

        [Test, Category("v8")]
        public void should_have_attempted_to_delete_emitted_streams()
        {
            Assert.IsFalse(
                _consumer.HandledMessages.OfType<ClientMessage.DeleteStream>().Any(x=>x.EventStreamId == "$projections-test-projection-stream1-checkpoint"));
        }
    }
}
