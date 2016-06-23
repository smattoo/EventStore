using EventStore.ClientAPI;
using EventStore.Common.Utils;
using EventStore.Core.Helpers;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Tests.ClientAPI;
using EventStore.Projections.Core.Services;
using EventStore.Projections.Core.Services.Processing;
using NUnit.Framework;
using System;

namespace EventStore.Projections.Core.Tests.Services.emitted_stream_manager
{
    public abstract class Base : SpecificationWithMiniNode
    {
        protected EmittedStreamManager _emittedStreamManager;
        protected ProjectionNamesBuilder _projectionNamesBuilder;
        protected ClientMessage.ReadStreamEventsForwardCompleted _readCompleted;
        protected IODispatcher _ioDispatcher;
        protected bool _trackEmittedStreams = true;
        protected string _projectionName = "test_projection";

        protected override void Given()
        {
            _ioDispatcher = new IODispatcher(_node.Node.MainQueue, new PublishEnvelope(_node.Node.MainQueue));
            _node.Node.MainBus.Subscribe(_ioDispatcher.BackwardReader);
            _node.Node.MainBus.Subscribe(_ioDispatcher.ForwardReader);
            _node.Node.MainBus.Subscribe(_ioDispatcher.Writer);
            _node.Node.MainBus.Subscribe(_ioDispatcher.StreamDeleter);
            _node.Node.MainBus.Subscribe(_ioDispatcher.Awaker);
            _node.Node.MainBus.Subscribe(_ioDispatcher);
            _projectionNamesBuilder = ProjectionNamesBuilder.CreateForTest(_projectionName);
            _emittedStreamManager = new EmittedStreamManager(_ioDispatcher, new ProjectionConfig(null, 1000, 1000 * 1000, 100, 500, true, true, false, false, false, _trackEmittedStreams), _projectionNamesBuilder);
        }
    }
}