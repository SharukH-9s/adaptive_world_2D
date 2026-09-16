using System;
using System.Collections.Generic;
using UnityEngine;

namespace AdaptiveWorld2D.Director
{
    /// <summary>
    /// Manages the queue of raw telemetry event JSON items and constructs batches for transport.
    /// Extracted from <see cref="AdaptiveDirectorClient"/> (Architecture Improvement Plan, Phase 5).
    /// </summary>
    public class TelemetryEventQueue
    {
        private readonly List<string> _rawEventsQueue = new List<string>();

        /// <summary>Number of queued events waiting to be flushed.</summary>
        public int Count => _rawEventsQueue.Count;

        /// <summary>
        /// Formats and adds a telemetry event to the queue using the provided timestamp.
        /// Preserves the exact JSON structure:
        /// {"event_type":"<eventType>","timestamp_s":<ts>,"data":<jsonData>}
        /// </summary>
        public void Enqueue(string eventType, string jsonData, float timestampSeconds)
        {
            string ts = timestampSeconds.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
            string item = "{\"event_type\":\"" + eventType + "\",\"timestamp_s\":" + ts + ",\"data\":" + jsonData + "}";
            _rawEventsQueue.Add(item);
        }

        /// <summary>
        /// Convenience overload that uses Unity's <see cref="Time.time"/>.
        /// </summary>
        public void Enqueue(string eventType, string jsonData)
        {
            Enqueue(eventType, jsonData, Time.time);
        }

        /// <summary>
        /// Drains all currently queued events and returns the formatted JSON payload:
        /// {"events":[<event1>,<event2>,...]}
        /// Returns null if the queue is empty.
        /// </summary>
        public string DequeueBatchPayload()
        {
            if (_rawEventsQueue.Count == 0) return null;

            List<string> toSend = new List<string>(_rawEventsQueue);
            _rawEventsQueue.Clear();

            return BuildBatchPayload(toSend);
        }

        /// <summary>
        /// Clears all events currently in the queue without generating a payload.
        /// </summary>
        public void Clear()
        {
            _rawEventsQueue.Clear();
        }

        /// <summary>
        /// Pure helper that wraps a collection of JSON event objects into a batch payload.
        /// </summary>
        public static string BuildBatchPayload(IEnumerable<string> jsonEvents)
        {
            return "{\"events\":[" + string.Join(",", jsonEvents) + "]}";
        }
    }
}
