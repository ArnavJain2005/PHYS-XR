using System.Collections.Generic;
using PHYSXR.Core.Data;

namespace PHYSXR.PhysicalWorld
{
    // Deduplicates PhysicalEvents.
    //
    // BOOT-AWARE DEDUP (fix P4): event IDs restart after an ESP32 reset, so
    // the key is (boot, eventId), not eventId alone. Otherwise event 1 of a
    // new boot was dropped as a duplicate of event 1 from the previous boot.
    // Legacy events carry boot 0, so for them the behaviour is unchanged.
    //
    // RegisterEvent is the original dedup-only API. Check/Accept add the
    // spec's OLD_EVENT rule for the live pipeline: within one boot, an event
    // whose ID is not above the highest accepted ID is refused.
    public class PhysicalEventManager
    {
        public enum Verdict
        {
            New = 0,
            Duplicate,
            Old
        }

        private readonly HashSet<(long boot, long id)> processedEvents = new HashSet<(long boot, long id)>();
        private readonly HashSet<long> processedIdsAnyBoot = new HashSet<long>();
        private readonly Dictionary<long, long> highestIdPerBoot = new Dictionary<long, long>();

        public int ProcessedCount => processedEvents.Count;

        public bool RegisterEvent(PhysicalEvent physicalEvent)
        {
            if (physicalEvent == null)
                return false;

            if (processedEvents.Contains((physicalEvent.boot, physicalEvent.eventId)))
                return false;

            Remember(physicalEvent);
            return true;
        }

        // Classifies an event without registering it.
        public Verdict Check(PhysicalEvent physicalEvent)
        {
            if (physicalEvent == null)
                return Verdict.Duplicate;

            if (processedEvents.Contains((physicalEvent.boot, physicalEvent.eventId)))
                return Verdict.Duplicate;

            if (highestIdPerBoot.TryGetValue(physicalEvent.boot, out long highest) && physicalEvent.eventId <= highest)
                return Verdict.Old;

            return Verdict.New;
        }

        // Registers the event if Check() says it is new; returns the verdict.
        public Verdict Accept(PhysicalEvent physicalEvent)
        {
            Verdict verdict = Check(physicalEvent);
            if (verdict == Verdict.New)
                Remember(physicalEvent);
            return verdict;
        }

        // Legacy query: true if this ID was processed in any boot.
        public bool HasProcessedEvent(long eventId)
        {
            return processedIdsAnyBoot.Contains(eventId);
        }

        public bool HasProcessedEvent(long boot, long eventId)
        {
            return processedEvents.Contains((boot, eventId));
        }

        private void Remember(PhysicalEvent physicalEvent)
        {
            processedEvents.Add((physicalEvent.boot, physicalEvent.eventId));
            processedIdsAnyBoot.Add(physicalEvent.eventId);

            if (!highestIdPerBoot.TryGetValue(physicalEvent.boot, out long highest) || physicalEvent.eventId > highest)
                highestIdPerBoot[physicalEvent.boot] = physicalEvent.eventId;
        }
    }
}
