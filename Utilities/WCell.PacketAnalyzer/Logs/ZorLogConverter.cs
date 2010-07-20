using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using WCell.Constants;
using WCell.Core.Network;
using WCell.PacketAnalysis.Updates;
using WCell.Util;

namespace WCell.PacketAnalysis.Logs
{
    /// <summary>
    /// A converter for Log-files, using the format that is generated by the Zor's .NET packet sniffer.
    /// </summary>
    public class ZorLogConverter
    {
        protected static Logger Log = LogManager.GetCurrentClassLogger();

        public static void Extract(string logFile, OpCodeValidator validator, Action<ParsablePacketInfo> parser)
        {
            Extract(logFile, new LogHandler(validator, parser));
        }

        /// <summary>
        /// Extracts all Packets out of the given logged and default-formatted lines
        /// </summary>
        public static void Extract(string logFile, params LogHandler[] handlers)
        {
            var file = File.Open(logFile, FileMode.Open, FileAccess.Read);
            var reader = new BinaryReader(file);

            while (reader.BaseStream.Position != reader.BaseStream.Length)
            {
                var type = reader.ReadByte();
                var opcode = (RealmServerOpCode)reader.ReadUInt16();
                var length = reader.ReadInt32();
                var direction = reader.ReadBoolean() ? PacketSender.Client : PacketSender.Server;
                var time = Utility.GetUTCTimeSeconds(reader.ReadUInt32());
                var data = reader.ReadBytes(length);

                // only do world packets (for now)
                if (type != 2)
                    continue;

                var opcodeHandlers = handlers.Where(handler => handler.Validator(opcode));
                if (opcodeHandlers.Count() <= 0)
                    continue;

                if (!Enum.IsDefined(typeof(RealmServerOpCode), opcode))
                {
                    Log.Warn("Packet had undefined Opcode: " + opcode);
                    continue;
                }

                var rawPacket = DisposableRealmPacketIn.Create(opcode, data);
                if (rawPacket != null)
                    foreach (var handler in opcodeHandlers)
                        handler.PacketParser(new ParsablePacketInfo(rawPacket, direction, time));
            }
        }

        /// <summary>
        /// Renders the given log file to the given output.
        /// </summary>
        /// <param name="logFile">The log file.</param>
        /// <param name="output">A StreamWriter or Console.Out etc</param>
        public static void ConvertLog(string logFile, TextWriter output)
        {
            var writer = new IndentTextWriter(output);
            Extract(logFile, LogConverter.DefaultValidator, info => LogConverter.ParsePacket(info, writer));
        }

        public static void ConvertLog(string logFile, string outputFile)
        {
            using (var stream = new StreamWriter(outputFile, false))
                ConvertLog(logFile, stream);
        }
    }
}