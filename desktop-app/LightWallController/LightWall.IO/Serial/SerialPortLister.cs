using System;
using System.IO.Ports;
using System.Linq;

namespace LightWall.IO.Serial
{
    /// <summary>
    /// Finds the serial ports available on this computer.
    ///
    /// Wrapped in its own small class rather than calling SerialPort directly
    /// from the interface, for two reasons: it keeps the window free of any
    /// knowledge of serial ports, and it gives one place to put the awkward
    /// details found below.
    /// </summary>
    public static class SerialPortLister
    {
        /// <summary>
        /// Lists the available port names, sorted so COM9 comes before COM10.
        ///
        /// WHY SORTING NEEDS CARE
        ///
        /// Sorted as plain text, "COM10" comes before "COM9", because the
        /// comparison stops at the first differing character and "1" is less
        /// than "9". In a dropdown that looks simply wrong.
        ///
        /// Sorting by the number instead puts them in the order a person
        /// expects. Names that do not fit the "COM" pattern - some USB adapters
        /// use other conventions - sort to the end alphabetically rather than
        /// being dropped.
        ///
        /// WHAT THIS CANNOT TELL YOU
        ///
        /// Which port is the Arduino. Windows reports names, not what is on the
        /// other end, so a machine with several serial devices will list them
        /// all with nothing to distinguish them.
        ///
        /// There is no clean fix. The practical approach is to try one, watch
        /// whether the wall responds, and try another if not - which is exactly
        /// what the bulb-identification mode is for.
        /// </summary>
        public static string[] GetAvailablePortNames()
        {
            string[] names;

            try
            {
                names = SerialPort.GetPortNames();
            }
            catch (Exception)
            {
                // Enumerating ports reads the registry and can fail on an
                // unusual system. An empty list is a far better outcome than the
                // app refusing to open.
                return Array.Empty<string>();
            }

            return names
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(TryGetPortNumber)
                .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        /// <summary>
        /// Pulls the number out of a name like "COM7", or returns a large value
        /// for anything that does not follow that pattern so it sorts last.
        /// </summary>
        private static int TryGetPortNumber(string portName)
        {
            if (portName.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(portName.AsSpan(3), out int number))
            {
                return number;
            }

            return int.MaxValue;
        }
    }
}
