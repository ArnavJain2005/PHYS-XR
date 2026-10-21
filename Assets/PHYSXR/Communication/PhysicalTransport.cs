namespace PHYSXR.Communication
{
    // How the ESP32 station's contract lines reach Unity. Both carry the same
    // contract v1 lines into the same SensorFeed/validator/pipeline; only the
    // framing differs (one '\n'-terminated line vs one datagram).
    //
    //   USB_SERIAL  the project's physical link (final report section 3.3):
    //               ESP32 USB-UART -> COM port, 115200 baud, Windows desktop only
    //   UDP         the Technical Spec's Wi-Fi option, port 47800; also what
    //               Tools/esp32_sim.py sends
    public enum PhysicalTransport
    {
        USB_SERIAL = 0,
        UDP = 1
    }
}
