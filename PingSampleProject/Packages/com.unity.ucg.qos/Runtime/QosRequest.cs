using System;
using Unity.Networking.Transport;
using UnityEngine;

namespace Unity.Networking.QoS
{
    public class QosRequest
    {
        public const int MinPacketLen = 15; // A packet with a 1 byte title is the bare minimum
        public const int MaxPacketLen = 1500; // Enough data to force packet fragmentation if you really want
        public const byte RequestMagic = 0x59;
        private const int ConstructedPacketLen = 14; // Packet length at object construction (contains no title)

        // Not making these public properties because we need to be able to take the address of them in Send()
        private byte m_Magic = RequestMagic;
        private byte m_VerAndFlow = 0x00;
        private byte m_TitleLen = 0x00;
        private byte[] m_Title = null;
        private byte m_Sequence = 0x00;
        private ushort m_Identifier = 0;
        private ulong m_Timestamp = 0;

        private ushort m_PacketLength = MinPacketLen - 1; // Still need a title

        // The public properties are backed by the private data above
        public byte Magic => m_Magic;

        public byte Version =>
            (byte) ((m_VerAndFlow >> 4) & 0x0F); // Won't implement set until there is a reason to do so

        public byte FlowControl => (byte) (m_VerAndFlow & 0x0F); // Never need to set flow control on the client

        public byte[] Title
        {
            // Title is odd because we get and set it as an array of bytes.  But it should be a UTF8-encoded string in
            // the guise of an array of bytes.
            get => m_Title;

            set
            {
                if (MinPacketLen + value.Length > MaxPacketLen)
                {
                    throw new ArgumentException(
                        $"Encoded title would make the QosPacket have size {MinPacketLen + value.Length}. Max size is {MaxPacketLen}.");
                }

                m_Title = value;
                m_TitleLen = (byte) (m_Title.Length + 1); // Title length includes the size byte
                m_PacketLength =
                    (ushort) (ConstructedPacketLen + m_Title.Length); // +1 is already included in the MinPacketLen
            }
        }

        public byte Sequence
        {
            get => m_Sequence;
            set => m_Sequence = value;
        }

        public ushort Identifier
        {
            get => m_Identifier;
            set => m_Identifier = value;
        }

        public ulong Timestamp
        {
            get => m_Timestamp;
            set => m_Timestamp = value;
        }

        public int Length => m_PacketLength; // Can't externally set the packet length

        /// <summary>
        /// Send the QosRequest packet to the given endpoint
        /// </summary>
        /// <param name="socket">Native socket descriptor</param>
        /// <param name="endpoint">Remote endpoint to send the request</param>
        /// <param name="expireTimeUtc">When to stop trying to send</param>
        /// <returns>
        /// (sent, errorcode) where sent is the number of bytes sent and errorcode is the error code if Send fails.
        /// 0 means no error.
        /// </returns>
        /// <exception cref="InvalidOperationException">Thrown if no title is set on the request.</exception>
        public (int, int) Send(long socket, ref NetworkEndPoint endpoint, DateTime expireTimeUtc)
        {
            int errorcode = 0;
            if (Title == null)
            {
                // A title will guarantee the packet length is sufficient
                throw new InvalidOperationException("QosRequest requires a title.");
            }

            unsafe
            {
                fixed (
                    void* pMagic = &m_Magic,
                    pVerAndFlow = &m_VerAndFlow,
                    pTitleLen = &m_TitleLen,
                    pTitle = &m_Title[0],
                    pSequence = &m_Sequence,
                    pIdentifier = &m_Identifier,
                    pTimestamp = &m_Timestamp
                )
                {
                    // No byte swizzling here since the required fields are one byte (or arrays of bytes), and the
                    // custom data is reflected back to us in the same format it was sent.
                    const int iovLen = 7;
                    var iov = stackalloc network_iovec[iovLen];

                    iov[0].buf = pMagic;
                    iov[0].len = sizeof(byte);

                    iov[1].buf = pVerAndFlow;
                    iov[1].len = sizeof(byte);

                    iov[2].buf = pTitleLen;
                    iov[2].len = sizeof(byte);

                    iov[3].buf = pTitle;
                    iov[3].len = m_Title.Length;

                    iov[4].buf = pSequence;
                    iov[4].len = sizeof(byte);

                    iov[5].buf = pIdentifier;
                    iov[5].len = sizeof(ushort);

                    // Everything below here is user-specified data and not part of the QosRequest header

                    iov[6].buf = pTimestamp;
                    iov[6].len = sizeof(ulong);

                    // WouldBlock is rare on send, but we'll handle it anyway.
                    int sent = -1;
                    do
                    {
                        sent = NativeBindings.network_sendmsg(socket, iov, iovLen, ref endpoint, ref errorcode);
                    } while (sent == -1 && QosHelper.WouldBlock(errorcode) && !QosHelper.ExpiredUtc(expireTimeUtc));

                    if (sent == -1)
                    {
                        Debug.LogError($"network_sendmsg returned {sent} with error code {errorcode}");
                    }
                    else if (sent != Length)
                    {
                        // TODO: This should be an error for the caller so they know this packet may not get a response.
                        Debug.LogError($"network_sendmsg only sent {sent} of {Length} bytes");
                    }

                    return (sent, errorcode);
                }
            }
        }
    }
}