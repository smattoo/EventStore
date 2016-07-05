﻿using EventStore.Common.Log;
using EventStore.Common.Utils;
using EventStore.Core.Data;
using EventStore.Core.Helpers;
using EventStore.Core.Messages;
using EventStore.Core.Services.UserManagement;
using System;
using System.Linq;

namespace EventStore.Projections.Core.Services.Processing
{
    public class EmittedStreamManager : IEmittedStreamManager
    {
        private static readonly ILogger Log = LogManager.GetLoggerFor<EmittedStreamManager>();
        private readonly IODispatcher _ioDispatcher;
        private readonly ProjectionConfig _projectionConfig;
        private readonly ProjectionNamesBuilder _projectionNamesBuilder;
        private readonly int _checkPointThreshold = 100;
        private int _numberOfEventsProcessed = 0;
        private const int RetryLimit = 3;
        private int _retryCount = RetryLimit;

        public EmittedStreamManager(IODispatcher ioDispatcher, ProjectionConfig projectionConfig, ProjectionNamesBuilder projectionNamesBuilder)
        {
            _ioDispatcher = ioDispatcher;
            _projectionConfig = projectionConfig;
            _projectionNamesBuilder = projectionNamesBuilder;
        }

        public void TrackEmittedStream(EmittedEvent[] emittedEvents)
        {
            if (!_projectionConfig.TrackEmittedStreams) return;
            foreach (var emittedEvent in emittedEvents)
            {
                var trackEvent = new Event(Guid.NewGuid(), ProjectionEventTypes.StreamTracked, false, Helper.UTF8NoBom.GetBytes(emittedEvent.StreamId), null);
                _ioDispatcher.WriteEvent(_projectionNamesBuilder.GetEmittedStreamsName(), ExpectedVersion.Any, trackEvent, SystemAccount.Principal, _ => { });
            }
        }

        public void DeleteEmittedStreams(Action onEmittedStreamsDeleted)
        {
            ReadLastCheckpoint(result => {
                var deleteFromPosition = GetPositionToDeleteFrom(result);
                DeleteEmittedStreamsFrom(deleteFromPosition, onEmittedStreamsDeleted);
            });
        }

        private void ReadLastCheckpoint(Action<ClientMessage.ReadStreamEventsBackwardCompleted> onReadCompleted)
        {
            _ioDispatcher.ReadBackward(_projectionNamesBuilder.GetEmittedStreamsCheckpointName(), -1, 1, false, SystemAccount.Principal, onReadCompleted);
        }

        private int GetPositionToDeleteFrom(ClientMessage.ReadStreamEventsBackwardCompleted onReadCompleted)
        {
            int deleteFromPosition = 0;
            if (onReadCompleted.Result == ReadStreamResult.Success)
            {
                if (onReadCompleted.Events.Length > 0)
                {
                    var checkpoint = onReadCompleted.Events.Where(v => v.Event.EventType == ProjectionEventTypes.ProjectionCheckpoint).Select(x => x.Event).FirstOrDefault();
                    if (checkpoint != null)
                    {
                        deleteFromPosition = checkpoint.Data.ParseJson<int>();
                    }
                }
            }
            return deleteFromPosition;
        }

        private void DeleteEmittedStreamsFrom(int fromPosition, Action onEmittedStreamsDeleted)
        {
            _ioDispatcher.ReadForward(_projectionNamesBuilder.GetEmittedStreamsName(), fromPosition, 1, false, SystemAccount.Principal, x => ReadCompleted(x, onEmittedStreamsDeleted));
        }

        private void ReadCompleted(ClientMessage.ReadStreamEventsForwardCompleted onReadCompleted, Action onEmittedStreamsDeleted)
        {
            if (onReadCompleted.Result == ReadStreamResult.Success || 
                onReadCompleted.Result == ReadStreamResult.NoStream)
            {
                if (onReadCompleted.Events.Length == 0 && !onReadCompleted.IsEndOfStream){
                    DeleteEmittedStreamsFrom(onReadCompleted.NextEventNumber, onEmittedStreamsDeleted);
                    return; 
                }
                if (onReadCompleted.Events.Length == 0)
                {
                    _ioDispatcher.DeleteStream(_projectionNamesBuilder.GetEmittedStreamsCheckpointName(), ExpectedVersion.Any, false, SystemAccount.Principal, x =>
                    {
                        if (x.Result == OperationResult.Success || x.Result == OperationResult.StreamDeleted)
                        {
                            Log.Info("PROJECTIONS: Projection Stream '{0}' deleted", _projectionNamesBuilder.GetEmittedStreamsCheckpointName());
                        }
                        else
                        {
                            Log.Error("PROJECTIONS: Failed to delete projection stream '{0}'. Reason: {1}", _projectionNamesBuilder.GetEmittedStreamsCheckpointName(), x.Result);
                        }
                        _ioDispatcher.DeleteStream(_projectionNamesBuilder.GetEmittedStreamsName(), ExpectedVersion.Any, false, SystemAccount.Principal, y =>
                        {
                            if (y.Result == OperationResult.Success || y.Result == OperationResult.StreamDeleted)
                            {
                                Log.Info("PROJECTIONS: Projection Stream '{0}' deleted", _projectionNamesBuilder.GetEmittedStreamsName());
                            }
                            else
                            {
                                Log.Error("PROJECTIONS: Failed to delete projection stream '{0}'. Reason: {1}", _projectionNamesBuilder.GetEmittedStreamsName(), y.Result);
                            }
                            onEmittedStreamsDeleted();
                        });
                    });
                }
                else
                {
                    var streamId = Helper.UTF8NoBom.GetString(onReadCompleted.Events[0].Event.Data);
                    _ioDispatcher.DeleteStream(streamId, ExpectedVersion.Any, false, SystemAccount.Principal, x => DeleteStreamCompleted(x, onEmittedStreamsDeleted, streamId, onReadCompleted.Events[0].OriginalEventNumber));
                }
            }
        }

        private void DeleteStreamCompleted(ClientMessage.DeleteStreamCompleted deleteStreamCompleted, Action onEmittedStreamsDeleted, string streamId, int eventNumber)
        {
            if (deleteStreamCompleted.Result == OperationResult.Success || deleteStreamCompleted.Result == OperationResult.StreamDeleted)
            {
                _retryCount = RetryLimit;
                _numberOfEventsProcessed++;
                if (_numberOfEventsProcessed >= _checkPointThreshold)
                {
                    _numberOfEventsProcessed = 0;
                    TryMarkCheckpoint(eventNumber);
                }
                DeleteEmittedStreamsFrom(eventNumber + 1, onEmittedStreamsDeleted);
            }
            else
            {
                if (_retryCount == 0)
                {
                    Log.Error("PROJECTIONS: Retry limit reached, could not delete stream: {0}. Manual intervention is required and you may need to delete this stream manually", streamId);
                    _retryCount = RetryLimit;
                    DeleteEmittedStreamsFrom(eventNumber + 1, onEmittedStreamsDeleted);
                    return;
                }
                Log.Error("PROJECTIONS: Failed to delete emitted stream {0}, Retrying ({1}/{2}). Reason: {3}", streamId, (RetryLimit - _retryCount) + 1, RetryLimit, deleteStreamCompleted.Result);
                _retryCount--;
                DeleteEmittedStreamsFrom(eventNumber, onEmittedStreamsDeleted);
            }
        }

        private void TryMarkCheckpoint(int eventNumber)
        {
            _ioDispatcher.WriteEvent(_projectionNamesBuilder.GetEmittedStreamsCheckpointName(), ExpectedVersion.Any, new Event(Guid.NewGuid(), "$Checkpoint", true, eventNumber.ToJson(), null), SystemAccount.Principal, x =>
            {
                if (x.Result == OperationResult.Success)
                {
                    Log.Debug("PROJECTIONS: Emitted Stream Deletion Checkpoint written at {0}", eventNumber);
                }
                else
                {
                    Log.Debug("PROJECTIONS: Emitted Stream Deletion Checkpoint Failed to be written at {0}", eventNumber);
                }
            });
        }
    }
}
