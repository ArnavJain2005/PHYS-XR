namespace PHYSXR.Communication
{
    // Where SensorFeed drains raw sensor messages from on the main thread.
    // Each item is one contract line (one UDP datagram, or one '\n'-framed
    // USB-serial line) with its Unity-side arrival time. Both transports feed
    // the same validator and pipeline through this, so nothing downstream can
    // tell which one a message came over.
    //
    // UDPReceiver.Datagram is the shared item type: it predates the serial
    // transport and the existing tests use it by that name.
    public interface IDatagramSource
    {
        bool TryDequeue(out UDPReceiver.Datagram datagram);
    }
}
