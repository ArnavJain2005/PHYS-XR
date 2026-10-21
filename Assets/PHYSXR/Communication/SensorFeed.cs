using System.Collections.Generic;
using PHYSXR.Core.Enums;

namespace PHYSXR.Communication
{
    // Main-thread pump between a transport's queue and the pipeline (spec
    // section B, step 3). Once per frame, Pump(now) drains every queued
    // datagram (bounded per frame), runs each through
    // PhysicalCommunicationPipeline.Process with its socket-thread arrival
    // time, and returns the outcomes for the caller to log and to forward
    // accepted events to the WorldStateManager.
    //
    // Plain C#: no Unity, no WorldState and no decision dependency. The
    // returned list is reused between calls; read it before the next Pump.
    public class SensorFeed
    {
        public const int DefaultMaxPerFrame = 256;

        private readonly IDatagramSource source;
        private readonly PhysicalCommunicationPipeline pipeline;
        private readonly List<PacketOutcome> outcomes = new List<PacketOutcome>();
        private readonly int maxPerFrame;

        public SensorFeed(UDPReceiver receiver, PhysicalCommunicationPipeline pipeline, int maxPerFrame = DefaultMaxPerFrame)
            : this((IDatagramSource)receiver, pipeline, maxPerFrame)
        {
            receiver.QueueOnReceive = true;
        }

        // Any transport: UDPReceiver, SerialLineReceiver, or a test source.
        public SensorFeed(IDatagramSource source, PhysicalCommunicationPipeline pipeline, int maxPerFrame = DefaultMaxPerFrame)
        {
            this.source = source;
            this.pipeline = pipeline;
            this.maxPerFrame = maxPerFrame;
        }

        // The UDP receiver when that is the source, otherwise null.
        public UDPReceiver Receiver => source as UDPReceiver;

        public IDatagramSource Source => source;
        public PhysicalCommunicationPipeline Pipeline => pipeline;

        public PhysicalSourceStatus Status { get; private set; } = PhysicalSourceStatus.UNAVAILABLE;

        // Set on the Pump in which Status changed (SOURCE_STATUS telemetry).
        public bool StatusChanged { get; private set; }
        public PhysicalSourceStatus PreviousStatus { get; private set; } = PhysicalSourceStatus.UNAVAILABLE;

        public IReadOnlyList<PacketOutcome> Pump(long nowMs)
        {
            outcomes.Clear();

            int handled = 0;
            while (handled < maxPerFrame && source.TryDequeue(out UDPReceiver.Datagram datagram))
            {
                outcomes.Add(pipeline.Process(datagram.data, datagram.arrivalMs));
                handled++;
            }

            PhysicalSourceStatus status = pipeline.GetSourceStatus(nowMs);
            StatusChanged = status != Status;
            PreviousStatus = Status;
            Status = status;
            return outcomes;
        }
    }
}
