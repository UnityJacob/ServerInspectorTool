using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace Unity.Ucg.MmConnector
{
    public sealed class QosConnector
    {
        static readonly Lazy<QosConnector> m_Instance = new Lazy<QosConnector>(() => new QosConnector());

        int m_NextProviderId;
        ConcurrentDictionary<int, Func<IList<QosResultMultiplay>>> m_Providers = new ConcurrentDictionary<int, Func<IList<QosResultMultiplay>>>();

        QosConnector() { }

        public static QosConnector Instance => m_Instance.Value;

        /// <summary>
        ///     Execute all registered providers and return the coalesced results in a single list
        /// </summary>
        /// <returns>QosTicketInfo with list of all results returned from all available providers</returns>
        public QosTicketInfo Execute()
        {
            var ticketInfo = new QosTicketInfo();
            foreach (var kvp in m_Providers)
            {
                IList<QosResultMultiplay> r;
                try
                {
                    r = kvp.Value();
                }
                catch
                {
                    // If we get an exception, it means the provider is no
                    // longer valid. Silently unregister this provider.  There
                    // is no error generated.
                    TryUnregisterProvider(kvp.Key);
                    continue;
                }

                if (r?.Count > 0)
                    ticketInfo.QosResults.AddRange(r);
            }

            return ticketInfo;
        }

        /// <summary>
        ///     Registers a provider to be called when Execute is called
        /// </summary>
        /// <param name="callback">Function that will be called on Execute</param>
        /// <returns>Unique provider ID</returns>
        public int RegisterProvider(Func<IList<QosResultMultiplay>> callback)
        {
            var id = Interlocked.Increment(ref m_NextProviderId);
            m_Providers[id] = callback;
            return id;
        }

        /// <summary>
        ///     Attempts to unregister a registerd provider
        /// </summary>
        /// <param name="id">ID returned during registration</param>
        /// <returns>True if provider was successfully unregistered. False if ID is invalid.</returns>
        public bool TryUnregisterProvider(int id)
        {
            return m_Providers.TryRemove(id, out _);
        }

        /// <summary>
        ///     Remove all registered providers
        /// </summary>
        public void Reset()
        {
            m_Providers = new ConcurrentDictionary<int, Func<IList<QosResultMultiplay>>>();
        }
    }
}
