using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Unity.Networking.QoS
{
    public struct QosStatsResult
    {
        public int LatencyMs;
        public float PacketLoss;

        public QosStatsResult(int latencyMs, float packetLoss)
        {
            LatencyMs = latencyMs;
            PacketLoss = packetLoss;
        }

        public bool IsValid() {
            return LatencyMs >= 0 && PacketLoss >= 0.0f;
        }
    }

    public class QosStats
    {
        // Weighted moving average of QoS Results.  A single weight can be specified, which is the weight to apply
        // to the newest QoS results.  The rest of the results equally share the remainder of the weight.
        private class WeightedMovingAverage
        {
            public QosStatsResult WeightedResult { get; private set; }

            private readonly QosStatsResult[] m_Results; // Array of results used to compute the moving average}
            private uint m_CurrentResult;                // Index to most recent result
            private uint m_NumResults;                   // Number of results we're currently tracking
            private readonly uint m_MaxResults;          // How many results to track in moving average
            private readonly float m_Weight;             // Weight of the most recent result 0.0 - 1.0

            // No default construction allowed
            private WeightedMovingAverage() {}

            public WeightedMovingAverage(uint numResults, float weightOfCurrentResult)
            {
                m_MaxResults = numResults;
                m_Weight = weightOfCurrentResult;

                // Initial results are flagged as invalid until we have some results.
                m_Results = new QosStatsResult[m_MaxResults];
                for (int i = 0; i < m_MaxResults; ++i)
                {
                    m_Results[i] = new QosStatsResult(-1, -1f);
                }
                WeightedResult = new QosStatsResult(-1, -1f);
           }

            public void AddResult(QosStatsResult result, bool updateAfterAdd = false)
            {
                if (m_NumResults < m_MaxResults) ++m_NumResults;

                m_CurrentResult = (m_CurrentResult + 1) % m_NumResults;
                m_Results[m_CurrentResult] = result;

                if (updateAfterAdd)
                    Update();
            }

            public void AllResults(out QosStatsResult[] results)
            {
                results = new QosStatsResult[m_Results.Length];
                m_Results.CopyTo(results, 0);
            }

            /// <summary>
            /// Update the weighted moving average for the available results.
            /// </summary>
            /// <remarks>
            /// Results are stored in <see cref="WeightedResult"/> if there are results to compute.
            /// </remarks>
            public void Update()
            {
                if (m_NumResults == 0)
                {
                    return; // Nothing to do
                }

                // The specified weight is a percentage [0.0f..1.0f], so we'll
                // use 100.0 as our "arbitrary" weighing value. Do not use a
                // weight if there is only one result.
                float currentResultWeight = ((m_NumResults == 1) ? 1.0f : m_Weight) * 100.0f;

                // Give the current result the appropriate weight.  We'll cast back to integer at the end.
                QosStatsResult currentResult = m_Results[m_CurrentResult];
                float weightedLatencyMs = currentResult.LatencyMs * (currentResultWeight/100.0f);
                float weightedPacketLoss = currentResult.PacketLoss * (currentResultWeight/100.0f);

                // Combine the remaining results and apply the remainder of the weight equally.
                if (m_NumResults > 1)
                {
                    float remainingWeightPerResult = (100.0f - currentResultWeight) / (m_NumResults - 1);
                    uint nextValidIndex = NextValidIndex(m_CurrentResult); // find the index of the next set of valid results.
                    for (int i = 0; i < m_NumResults - 1; ++i) // m_NumResults-1 so we don't include the currentResult again
                    {
                        QosStatsResult result = m_Results[(nextValidIndex + i) % m_MaxResults];
                        weightedLatencyMs += result.LatencyMs * (remainingWeightPerResult / 100.0f);
                        weightedPacketLoss += result.PacketLoss * (remainingWeightPerResult/100.0f);
                    }
                }

                WeightedResult = new QosStatsResult(
                    Mathf.RoundToInt(weightedLatencyMs),
                    Mathf.Min(weightedPacketLoss, 1.0f));  // Make sure precision issues don't yield >100% packet loss
            }

            /// <summary>
            /// Return the next valid index into <see cref="m_Results"/> after the given index.
            /// </summary>
            /// <param name="startIndex">Where to start the search for the next index</param>
            /// <returns>
            /// Index of the next valid result in the array.  The valid index may be less than the startIndex.
            /// </returns>
            /// <exception cref="KeyNotFoundException">Thrown if no valid result is found before circling back around to the startIndex</exception>
            private uint NextValidIndex(uint startIndex)
            {
                uint nextIndex = (startIndex + 1) % m_NumResults;
                while (nextIndex != startIndex && !m_Results[nextIndex].IsValid()) {
                    nextIndex = (nextIndex + 1) % m_NumResults;
                }

                if (nextIndex == startIndex) throw new KeyNotFoundException("No valid index found.");
                return nextIndex;
            }
        }

        private ReaderWriterLockSlim m_ResultsLock = new ReaderWriterLockSlim();

        // IP:Port => Weighted results
        private Dictionary<string, WeightedMovingAverage> m_Results = new Dictionary<string, WeightedMovingAverage>();

        private readonly uint m_NumResults;
        private readonly float m_Weight;

        // Restrict default construction
        private QosStats() {}

        public QosStats(uint numResults, float weightOfCurrentResult)
        {
            if (numResults == 0) throw new ArgumentOutOfRangeException("Number of results to track must be positive");
            if (weightOfCurrentResult < 0.0f || weightOfCurrentResult > 1.0f) throw new ArgumentOutOfRangeException("Weight must be in the range [0.0f..1.0f]");

            m_NumResults = numResults;
            m_Weight = weightOfCurrentResult;
        }

        /// <summary>
        /// Add a result to the weighted rolling average history.
        /// </summary>
        /// <param name="ipAndPort">IP:Port string to identify where the results go</param>
        /// <param name="result">The result to add to the history. This result becomes the current result.</param>
        public void AddResult(string ipAndPort, QosResult result)
        {
            if (result.PacketLoss + Mathf.Epsilon < 0.0f || result.PacketLoss - Mathf.Epsilon > 1.0f) throw new ArgumentOutOfRangeException(nameof(result), "PacketLoss must be in the range [0.0..1.0]");

            m_ResultsLock.EnterWriteLock();
            try
            {
                WeightedMovingAverage wma = null;
                if (!m_Results.TryGetValue(ipAndPort, out wma))
                {
                    // Tracking new server
                    wma = new WeightedMovingAverage(m_NumResults, m_Weight);
                }

                wma.AddResult(new QosStatsResult((int)result.AverageLatencyMs, result.PacketLoss));
                m_Results[ipAndPort] = wma;
            }
            finally
            {
                m_ResultsLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Get the weighted rolling average for the given key.
        /// </summary>
        /// <param name="ipAndPort">IP:Port string to get results for</param>
        /// <param name="result">The weighted rolling average for the given ipAndPort</param>
        /// <returns>true if the record was found, false otherwise</returns>
        public bool TryGetWeightedAverage(string ipAndPort, out QosStatsResult result)
        {
            m_ResultsLock.EnterReadLock();
            try
            {
                if (!m_Results.TryGetValue(ipAndPort, out WeightedMovingAverage wma))
                {
                    result = new QosStatsResult(-1, -1f); // Make result invalid
                    return false;
                }

                wma.Update();
                result = wma.WeightedResult.IsValid() ? wma.WeightedResult : new QosStatsResult(-1, -1f);
                return result.IsValid();
            }
            finally
            {
                m_ResultsLock.ExitReadLock();
            }
        }

        /// <summary>
        /// Get an array of all the results currently being tracked for the given key.
        /// </summary>
        /// <param name="ipAndPort">IP:Port string to get results for</param>
        /// <param name="results">Array of results (unweighted) used to compute the weighted average.</param>
        /// <returns>true if record(s) were found, false if key does not exist or no valid records found</returns>
        /// <remarks>There is no way to determine which results correspond to the most recently added results.</remarks>
        public bool TryGetAllResults(string ipAndPort, out QosStatsResult[] results)
        {
            m_ResultsLock.EnterReadLock();
            try
            {
                if (!m_Results.TryGetValue(ipAndPort, out WeightedMovingAverage wma))
                {
                    results = new QosStatsResult[0];
                    return false;
                }

                wma.AllResults(out results);
                return results.Length > 0;
            }
            finally
            {
                m_ResultsLock.ExitReadLock();
            }
        }

    }
}