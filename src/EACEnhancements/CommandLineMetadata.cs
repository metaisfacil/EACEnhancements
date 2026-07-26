using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using HelperFunctionsLib;
using Microsoft.Win32;

namespace AudioDataPlugIn
{
    internal sealed class CommandLineInvocation
    {
        internal bool RunHundredPercentLog;
        internal string HtoaFilename;
        internal string Drive;
        internal string Destination;
        internal CommandLineMetadata Metadata;

        internal static CommandLineInvocation Parse(string[] arguments)
        {
            CommandLineInvocation result = new CommandLineInvocation();
            string encodedMetadata = null;
            string drive = null;
            string destination = null;
            string htoaFilename = null;

            for (int i = 1; i < arguments.Length; i++)
            {
                string argument = arguments[i] ?? String.Empty;
                if (String.Equals(argument, "--eace-100-log", StringComparison.OrdinalIgnoreCase))
                {
                    if (result.RunHundredPercentLog)
                        throw new FormatException("--eace-100-log was specified more than once.");
                    result.RunHundredPercentLog = true;
                }
                else if (argument.StartsWith("--eace-metadata=", StringComparison.OrdinalIgnoreCase))
                {
                    if (encodedMetadata != null)
                        throw new FormatException("--eace-metadata was specified more than once.");
                    encodedMetadata = argument.Substring("--eace-metadata=".Length);
                }
                else if (argument.StartsWith("--eace-htoa=", StringComparison.OrdinalIgnoreCase))
                {
                    if (htoaFilename != null)
                        throw new FormatException("--eace-htoa was specified more than once.");
                    htoaFilename = argument.Substring("--eace-htoa=".Length).Trim();
                    if (!IsValidHtoaFilename(htoaFilename))
                        throw new FormatException("--eace-htoa requires a filename without a directory path.");
                }
                else if (argument.StartsWith("--eace-drive=", StringComparison.OrdinalIgnoreCase))
                {
                    if (drive != null)
                        throw new FormatException("--eace-drive was specified more than once.");
                    drive = argument.Substring("--eace-drive=".Length).Trim();
                    if (drive.Length == 0)
                        throw new FormatException("--eace-drive requires a drive letter or EAC drive name.");
                }
                else if (argument.StartsWith("--eace-dest=", StringComparison.OrdinalIgnoreCase))
                {
                    if (destination != null)
                        throw new FormatException("--eace-dest was specified more than once.");
                    destination = argument.Substring("--eace-dest=".Length).Trim();
                    if (destination.Length == 0)
                        throw new FormatException("--eace-dest requires an absolute album-folder path.");
                    if (!IsFullyQualifiedDestination(destination))
                        throw new FormatException("--eace-dest must be a fully qualified path.");
                }
            }

            if (encodedMetadata != null)
                result.Metadata = D1MetadataCodec.Decode(encodedMetadata);
            result.Drive = drive;
            result.Destination = destination;
            result.HtoaFilename = htoaFilename;
            if (result.RunHundredPercentLog && result.Metadata == null)
                throw new FormatException("--eace-100-log requires --eace-metadata=d1.<payload>.");
            if (result.Drive != null &&
                result.Metadata == null &&
                result.HtoaFilename == null)
            {
                throw new FormatException(
                    "--eace-drive requires --eace-metadata=d1.<payload> or --eace-htoa=FILENAME.ext.");
            }
            if (result.Destination != null &&
                (!result.RunHundredPercentLog || result.Metadata == null))
            {
                throw new FormatException(
                    "--eace-dest requires --eace-100-log and --eace-metadata=d1.<payload>.");
            }
            return result;
        }

        internal static bool IsValidHtoaFilename(string value)
        {
            string text = (value ?? String.Empty).Trim();
            if (text.Length == 0 ||
                text == "." ||
                text == ".." ||
                Path.IsPathRooted(text) ||
                text.IndexOf('\\') >= 0 ||
                text.IndexOf('/') >= 0)
            {
                return false;
            }
            return String.Equals(
                Path.GetFileName(text),
                text,
                StringComparison.Ordinal);
        }

        internal static bool IsFullyQualifiedDestination(string value)
        {
            string text = (value ?? String.Empty).Trim();
            if (Regex.IsMatch(text, "^[A-Za-z]:[\\\\/]"))
                return true;
            if (!text.StartsWith(@"\\", StringComparison.Ordinal))
                return false;
            string[] components = text.Substring(2).Split(
                new[] { '\\', '/' },
                StringSplitOptions.RemoveEmptyEntries);
            return components.Length >= 2;
        }

        internal bool HasWork
        {
            get { return Metadata != null || HtoaFilename != null; }
        }
    }

    internal sealed class CommandLineMetadata
    {
        internal int TrackCount;
        internal uint? CddbId;
        internal uint? LeadoutPosition;
        internal uint[] TrackStartPositions;
        internal string AlbumArtist = String.Empty;
        internal string AlbumTitle = String.Empty;
        internal int CddbMusicType = -1;
        internal int Year = -1;
        internal int Revision = -1;
        internal int Mp3Type = -1;
        internal string ExtendedDiscInformation = String.Empty;
        internal string Mp3V2Type = String.Empty;
        internal int FirstTrackNumber = 1;
        internal string AlbumInterpret = String.Empty;
        internal int CdNumber = 1;
        internal int TotalNumberOfCds = 1;
        internal string AlbumComposer = String.Empty;
        internal string Label = String.Empty;
        internal string Barcode = String.Empty;
        internal string CatalogNumber = String.Empty;
        internal string CoverImageUrl = String.Empty;
        internal byte[] CoverImage;
        internal CommandLineTrackMetadata[] Tracks;
    }

    internal sealed class CommandLineTrackMetadata
    {
        internal int Number;
        internal string Title = String.Empty;
        internal string ExtendedInformation = String.Empty;
        internal string Artist = String.Empty;
        internal string Composer = String.Empty;
        internal string Lyrics = String.Empty;
        internal uint? StartPosition;
        internal uint? EndPosition;
        internal bool? Preemphasis;
        internal bool? DataTrack;
        internal bool? FourChannels;
    }

    internal static class D1MetadataCodec
    {
        private const int MaximumDecodedBytes = 1024 * 1024;
        private static readonly HashSet<string> RootFields = Fields("disc", "tracks");
        private static readonly HashSet<string> DiscFields = Fields(
            "trackCount", "cddbId", "leadoutPosition", "trackStartPositions",
            "albumArtist", "albumTitle", "cddbMusicType", "year", "revision",
            "mp3Type", "extendedDiscInformation", "mp3V2Type", "firstTrackNumber",
            "albumInterpret", "cdNumber", "totalNumberOfCds", "albumComposer",
            "label", "barcode", "catalogNumber",
            "coverImageUrl", "coverImageBase64");
        private static readonly HashSet<string> TrackFields = Fields(
            "number", "title", "extendedInformation", "artist", "composer", "lyrics",
            "startPosition", "endPosition", "preemphasis", "dataTrack", "fourChannels");

        internal static CommandLineMetadata Decode(string value)
        {
            if (String.IsNullOrEmpty(value) || !value.StartsWith("d1.", StringComparison.Ordinal))
                throw new FormatException("--eace-metadata must use the d1. format.");

            byte[] compressed = DecodeBase64Url(value.Substring(3));
            string json;
            using (MemoryStream input = new MemoryStream(compressed, false))
            using (DeflateStream inflater = new DeflateStream(input, CompressionMode.Decompress))
            using (MemoryStream output = new MemoryStream())
            {
                byte[] buffer = new byte[8192];
                for (;;)
                {
                    int read = inflater.Read(buffer, 0, buffer.Length);
                    if (read == 0)
                        break;
                    if (output.Length + read > MaximumDecodedBytes)
                        throw new FormatException("The decoded metadata exceeds 1 MiB.");
                    output.Write(buffer, 0, read);
                }
                json = new UTF8Encoding(false, true).GetString(output.ToArray());
            }

            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = MaximumDecodedBytes;
            serializer.RecursionLimit = 64;
            object parsed;
            try
            {
                parsed = serializer.DeserializeObject(json);
            }
            catch (Exception error)
            {
                throw new FormatException("The d1. payload does not contain valid JSON.", error);
            }
            return ParseDocument(AsObject(parsed, "metadata"));
        }

        private static CommandLineMetadata ParseDocument(Dictionary<string, object> root)
        {
            RejectUnknownFields(root, RootFields, "metadata");
            Dictionary<string, object> disc = AsObject(Required(root, "disc", "metadata"), "disc");
            RejectUnknownFields(disc, DiscFields, "disc");

            CommandLineMetadata result = new CommandLineMetadata();
            result.TrackCount = GetRequiredInt(disc, "trackCount", "disc");
            if (result.TrackCount < 1 || result.TrackCount > 100)
                throw new FormatException("disc.trackCount must be between 1 and 100.");
            result.CddbId = GetOptionalUInt(disc, "cddbId", true);
            result.LeadoutPosition = GetOptionalUInt(disc, "leadoutPosition", false);
            result.TrackStartPositions = GetOptionalUIntArray(disc, "trackStartPositions");
            if (result.TrackStartPositions != null && result.TrackStartPositions.Length != result.TrackCount)
                throw new FormatException("disc.trackStartPositions must contain one value per track.");

            result.AlbumArtist = GetString(disc, "albumArtist");
            result.AlbumTitle = GetString(disc, "albumTitle");
            result.CddbMusicType = GetInt(disc, "cddbMusicType", -1);
            result.Year = GetInt(disc, "year", -1);
            result.Revision = GetInt(disc, "revision", -1);
            result.Mp3Type = GetInt(disc, "mp3Type", -1);
            result.ExtendedDiscInformation = GetString(disc, "extendedDiscInformation");
            result.Mp3V2Type = GetString(disc, "mp3V2Type");
            result.FirstTrackNumber = GetInt(disc, "firstTrackNumber", 1);
            result.AlbumInterpret = GetString(disc, "albumInterpret");
            result.CdNumber = GetInt(disc, "cdNumber", 1);
            result.TotalNumberOfCds = GetInt(disc, "totalNumberOfCds", 1);
            result.AlbumComposer = GetString(disc, "albumComposer");
            result.Label = GetString(disc, "label");
            result.Barcode = GetString(disc, "barcode");
            result.CatalogNumber = GetString(disc, "catalogNumber");
            result.CoverImageUrl = GetString(disc, "coverImageUrl");
            string cover = GetString(disc, "coverImageBase64");
            if (cover.Length != 0)
            {
                try { result.CoverImage = Convert.FromBase64String(cover); }
                catch (FormatException error) { throw new FormatException("disc.coverImageBase64 is not valid base64.", error); }
            }

            object tracksValue = Required(root, "tracks", "metadata");
            object[] tracks = tracksValue as object[];
            if (tracks == null)
            {
                System.Collections.ArrayList list = tracksValue as System.Collections.ArrayList;
                if (list != null)
                    tracks = list.ToArray();
            }
            if (tracks == null || tracks.Length != result.TrackCount)
                throw new FormatException("metadata.tracks must contain exactly disc.trackCount entries.");

            result.Tracks = new CommandLineTrackMetadata[result.TrackCount];
            for (int i = 0; i < tracks.Length; i++)
            {
                Dictionary<string, object> trackObject = AsObject(tracks[i], "tracks[" + i + "]");
                RejectUnknownFields(trackObject, TrackFields, "tracks[" + i + "]");
                CommandLineTrackMetadata track = new CommandLineTrackMetadata();
                track.Number = GetInt(trackObject, "number", i + 1);
                if (track.Number < 1 || track.Number > result.TrackCount || result.Tracks[track.Number - 1] != null)
                    throw new FormatException("Track numbers must be unique and between 1 and disc.trackCount.");
                track.Title = GetString(trackObject, "title");
                track.ExtendedInformation = GetString(trackObject, "extendedInformation");
                track.Artist = GetString(trackObject, "artist");
                track.Composer = GetString(trackObject, "composer");
                track.Lyrics = GetString(trackObject, "lyrics");
                track.StartPosition = GetOptionalUInt(trackObject, "startPosition", false);
                track.EndPosition = GetOptionalUInt(trackObject, "endPosition", false);
                track.Preemphasis = GetOptionalBool(trackObject, "preemphasis");
                track.DataTrack = GetOptionalBool(trackObject, "dataTrack");
                track.FourChannels = GetOptionalBool(trackObject, "fourChannels");
                result.Tracks[track.Number - 1] = track;
            }
            return result;
        }

        private static byte[] DecodeBase64Url(string text)
        {
            if (text.Length == 0)
                throw new FormatException("The d1. payload is empty.");
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') ||
                      (c >= '0' && c <= '9') || c == '-' || c == '_'))
                    throw new FormatException("The d1. payload is not unpadded Base64url.");
            }
            if ((text.Length & 3) == 1)
                throw new FormatException("The d1. payload has an invalid Base64url length.");
            string padded = text.Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - (padded.Length & 3)) & 3);
            try { return Convert.FromBase64String(padded); }
            catch (FormatException error) { throw new FormatException("The d1. payload is not valid Base64url.", error); }
        }

        private static Dictionary<string, object> AsObject(object value, string location)
        {
            Dictionary<string, object> result = value as Dictionary<string, object>;
            if (result == null)
                throw new FormatException(location + " must be a JSON object.");
            return result;
        }

        private static object Required(Dictionary<string, object> values, string name, string location)
        {
            object value;
            if (!values.TryGetValue(name, out value) || value == null)
                throw new FormatException(location + "." + name + " is required.");
            return value;
        }

        private static string GetString(Dictionary<string, object> values, string name)
        {
            object value;
            if (!values.TryGetValue(name, out value) || value == null)
                return String.Empty;
            string text = value as string;
            if (text == null)
                throw new FormatException(name + " must be a string or null.");
            return text;
        }

        private static int GetRequiredInt(Dictionary<string, object> values, string name, string location)
        {
            return ConvertInt(Required(values, name, location), location + "." + name);
        }

        private static int GetInt(Dictionary<string, object> values, string name, int defaultValue)
        {
            object value;
            return !values.TryGetValue(name, out value) || value == null
                ? defaultValue : ConvertInt(value, name);
        }

        private static int ConvertInt(object value, string location)
        {
            try
            {
                decimal number = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                if (number != Decimal.Truncate(number) || number < Int32.MinValue || number > Int32.MaxValue)
                    throw new FormatException();
                return Decimal.ToInt32(number);
            }
            catch (Exception error)
            {
                if (error is FormatException || error is InvalidCastException || error is OverflowException)
                    throw new FormatException(location + " must be a 32-bit integer.", error);
                throw;
            }
        }

        private static uint? GetOptionalUInt(Dictionary<string, object> values, string name, bool allowHexString)
        {
            object value;
            if (!values.TryGetValue(name, out value) || value == null)
                return null;
            try
            {
                string text = value as string;
                if (text != null)
                {
                    if (!allowHexString)
                        throw new FormatException();
                    if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                        text = text.Substring(2);
                    return UInt32.Parse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
                }
                decimal number = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                if (number != Decimal.Truncate(number) || number < UInt32.MinValue || number > UInt32.MaxValue)
                    throw new FormatException();
                return Decimal.ToUInt32(number);
            }
            catch (Exception error)
            {
                if (error is FormatException || error is InvalidCastException || error is OverflowException)
                    throw new FormatException(name + " must be an unsigned 32-bit integer" +
                        (allowHexString ? " or a hexadecimal string" : String.Empty) + ".", error);
                throw;
            }
        }

        private static uint[] GetOptionalUIntArray(Dictionary<string, object> values, string name)
        {
            object value;
            if (!values.TryGetValue(name, out value) || value == null)
                return null;
            object[] array = value as object[];
            if (array == null)
            {
                System.Collections.ArrayList list = value as System.Collections.ArrayList;
                if (list != null)
                    array = list.ToArray();
            }
            if (array == null)
                throw new FormatException(name + " must be an array.");
            uint[] result = new uint[array.Length];
            for (int i = 0; i < array.Length; i++)
            {
                Dictionary<string, object> holder = new Dictionary<string, object>();
                holder.Add(name, array[i]);
                result[i] = GetOptionalUInt(holder, name, false).Value;
            }
            return result;
        }

        private static bool? GetOptionalBool(Dictionary<string, object> values, string name)
        {
            object value;
            if (!values.TryGetValue(name, out value) || value == null)
                return null;
            if (!(value is bool))
                throw new FormatException(name + " must be true, false, or null.");
            return (bool)value;
        }

        private static void RejectUnknownFields(
            Dictionary<string, object> values,
            HashSet<string> allowed,
            string location)
        {
            foreach (string name in values.Keys)
            {
                if (!allowed.Contains(name))
                    throw new FormatException("Unknown " + location + " field: " + name + ".");
            }
        }

        private static HashSet<string> Fields(params string[] names)
        {
            return new HashSet<string>(names, StringComparer.Ordinal);
        }
    }

    internal static partial class EnhancementRuntime
    {
        internal const string CommandLineMetadataProviderGuid =
            "2D2235AB-0876-44F9-9CD2-DF2D3D06EB3C";
        internal const uint StartCommandLineRequestCommand = 0xA322;
        internal const uint BeginCommandLineMetadataCommand = 0xA323;
        internal const uint FinishCommandLineMetadataCommand = 0xA324;
        internal const uint FailCommandLineMetadataCommand = 0xA325;
        internal const uint FinishCommandLineRunCommand = 0xA326;
        internal const uint ContinueCommandLineActionsCommand = 0xA327;
        private const int DriveSelectorControlId = 5;
        private const int DriveReadyWaitAttempts = 600;
        private const uint Eac18MetadataReplacementGuardVa = 0x0040875A;
        private const uint Eac18MetadataReplacementAcceptedVa = 0x0040879D;
        private const uint Eac16MetadataReplacementGuardVa = 0x00408576;
        private const uint Eac16MetadataReplacementAcceptedVa = 0x004085B9;
        private const int MetadataLookupCommand = 525;
        private static readonly byte[] ExpectedMetadataReplacementGuard =
            { 0x3C, 0x00, 0x75, 0x0B, 0x80, 0x3D };
        private static readonly object CommandLineReplacementPatchLock =
            new object();
        private const string InternetOptionsKey = @"Software\AWSoftware\EACU\Internet Options";
        private static CommandLineInvocation commandLineInvocation;
        private static bool commandLineRequestPresent;
        private static string commandLineError;
        private static int commandLineStartPosted;
        private static int commandLineLookupPosted;
        private static int commandLineProviderCalled;
        private static int commandLineHtoaAttempted;
        private static int commandLineOriginalProviderIndex = -1;
        private static uint commandLineReplacementPatchVa;
        private static byte[] commandLineReplacementOriginalBytes;

        private static void InitializeCommandLine()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int i = 1; i < arguments.Length; i++)
            {
                string argument = arguments[i] ?? String.Empty;
                if (String.Equals(
                        argument,
                        "--eace-100-log",
                        StringComparison.OrdinalIgnoreCase) ||
                    argument.StartsWith(
                        "--eace-metadata=",
                        StringComparison.OrdinalIgnoreCase) ||
                    argument.StartsWith(
                        "--eace-drive=",
                        StringComparison.OrdinalIgnoreCase) ||
                    argument.StartsWith(
                        "--eace-htoa=",
                        StringComparison.OrdinalIgnoreCase) ||
                    argument.StartsWith(
                        "--eace-dest=",
                        StringComparison.OrdinalIgnoreCase))
                {
                    commandLineRequestPresent = true;
                    break;
                }
            }
            try
            {
                commandLineInvocation =
                    CommandLineInvocation.Parse(arguments);
            }
            catch (Exception error)
            {
                commandLineError = error.Message;
                Log("Command-line validation failed: " + error);
            }
        }

        private static void BeginCommandLineWhenReady(IntPtr mainWindow)
        {
            if (commandLineError == null && (commandLineInvocation == null || !commandLineInvocation.HasWork))
                return;

            if (Interlocked.CompareExchange(ref commandLineStartPosted, 1, 0) == 0)
            {
                NativeMethods.PostMessageW(mainWindow, NativeMethods.WM_COMMAND,
                    new IntPtr(StartCommandLineRequestCommand), IntPtr.Zero);
            }
        }

        private static void StartCommandLineRequest(IntPtr mainWindow)
        {
            if (commandLineError != null)
            {
                ShowCommandLineError(mainWindow);
                return;
            }

            try
            {
                if (!String.IsNullOrEmpty(commandLineInvocation.Drive))
                {
                    SelectCommandLineDrive(mainWindow, commandLineInvocation.Drive);
                    EnsureCommandLineDriveHasMedia(commandLineInvocation.Drive);
                }
            }
            catch (Exception error)
            {
                commandLineError = error.Message;
                Log("Command-line drive selection failed: " + error);
                ShowCommandLineError(mainWindow);
                return;
            }

            Thread thread = new Thread(delegate()
            {
                for (int i = 0;
                    i < DriveReadyWaitAttempts && NativeMethods.IsWindow(mainWindow);
                    i++)
                {
                    if (IsReferenceRipCommandEnabled(mainWindow))
                    {
                        if (Interlocked.CompareExchange(
                                ref commandLineLookupPosted,
                                1,
                                0) == 0)
                        {
                            NativeMethods.PostMessageW(
                                mainWindow,
                                NativeMethods.WM_COMMAND,
                                new IntPtr(
                                    commandLineInvocation.Metadata == null
                                        ? ContinueCommandLineActionsCommand
                                        : BeginCommandLineMetadataCommand),
                                IntPtr.Zero);
                        }
                        return;
                    }
                    Thread.Sleep(200);
                }
                commandLineError = "No ready audio CD was detected within two minutes" +
                    (String.IsNullOrEmpty(commandLineInvocation.Drive)
                        ? "."
                        : " in drive '" + commandLineInvocation.Drive + "'.");
                NativeMethods.PostMessageW(
                    mainWindow,
                    NativeMethods.WM_COMMAND,
                    new IntPtr(FailCommandLineMetadataCommand),
                    IntPtr.Zero);
            });
            thread.IsBackground = true;
            thread.Name = "EAC Enhancements command-line drive waiter";
            thread.Start();
        }

        private static bool IsReferenceRipCommandEnabled(IntPtr mainWindow)
        {
            IntPtr menu = NativeMethods.GetMenu(mainWindow);
            if (menu == IntPtr.Zero)
                return false;
            IntPtr owner = FindMenuContainingCommand(menu, ReferenceRipCommand);
            if (owner == IntPtr.Zero)
                return false;
            uint state = NativeMethods.GetMenuState(owner, ReferenceRipCommand, NativeMethods.MF_BYCOMMAND);
            return IsReferenceRipCommandStateEnabled(state);
        }

        internal static bool IsReferenceRipCommandStateEnabled(uint state)
        {
            return state != UInt32.MaxValue &&
                (state & (NativeMethods.MF_DISABLED | NativeMethods.MF_GRAYED)) == 0;
        }

        private static void StartCommandLineMetadataLookup(IntPtr mainWindow)
        {
            if (commandLineError != null)
            {
                ShowCommandLineError(mainWindow);
                return;
            }

            string error;
            if (!MetadataProviderBridge.TryActivate(out commandLineOriginalProviderIndex, out error))
            {
                commandLineError = error;
                ShowCommandLineError(mainWindow);
                return;
            }

            Interlocked.Exchange(ref commandLineProviderCalled, 0);
            try
            {
                InstallCommandLineMetadataReplacementBypass();
            }
            catch (Exception bypassError)
            {
                MetadataProviderBridge.Restore(
                    commandLineOriginalProviderIndex);
                commandLineOriginalProviderIndex = -1;
                commandLineError =
                    "EAC's metadata replacement confirmation could not be " +
                    "safely bypassed. " + bypassError.Message;
                Log(
                    "Command-line metadata replacement bypass failed: " +
                    bypassError);
                ShowCommandLineError(mainWindow);
                return;
            }
            if (!NativeMethods.PostMessageW(mainWindow, NativeMethods.WM_COMMAND,
                new IntPtr(MetadataLookupCommand), IntPtr.Zero))
            {
                RestoreCommandLineMetadataReplacementBypass();
                MetadataProviderBridge.Restore(commandLineOriginalProviderIndex);
                commandLineError = "EAC rejected the metadata lookup command.";
                ShowCommandLineError(mainWindow);
                return;
            }

            Thread watchdog = new Thread(delegate()
            {
                Thread.Sleep(30000);
                if (Interlocked.CompareExchange(ref commandLineProviderCalled, 0, 0) == 0)
                {
                    commandLineError = "EAC did not request the command-line metadata within 30 seconds.";
                    NativeMethods.PostMessageW(mainWindow, NativeMethods.WM_COMMAND,
                        new IntPtr(FailCommandLineMetadataCommand), IntPtr.Zero);
                }
            });
            watchdog.IsBackground = true;
            watchdog.Name = "EAC Enhancements metadata watchdog";
            watchdog.Start();
        }

        private static void InstallCommandLineMetadataReplacementBypass()
        {
            lock (CommandLineReplacementPatchLock)
            {
                if (commandLineReplacementOriginalBytes != null)
                    return;
                uint guard;
                uint accepted;
                SelectCommandLineMetadataReplacementAddresses(
                    layout == null ? null : layout.Name,
                    out guard,
                    out accepted);
                RequireBytes(
                    guard,
                    ExpectedMetadataReplacementGuard,
                    "metadata replacement confirmation guard");
                commandLineReplacementOriginalBytes = ReadBytes(
                    guard,
                    ExpectedMetadataReplacementGuard.Length);
                commandLineReplacementPatchVa = guard;
                try
                {
                    WriteJumpPatch(
                        guard,
                        accepted,
                        ExpectedMetadataReplacementGuard.Length);
                }
                catch
                {
                    commandLineReplacementOriginalBytes = null;
                    commandLineReplacementPatchVa = 0;
                    throw;
                }
                Log(
                    "Temporarily bypassed EAC's existing-metadata " +
                    "confirmation for the command-line request.");
            }
        }

        private static void RestoreCommandLineMetadataReplacementBypass()
        {
            lock (CommandLineReplacementPatchLock)
            {
                if (commandLineReplacementOriginalBytes == null)
                    return;
                WriteMemoryPatch(
                    commandLineReplacementPatchVa,
                    commandLineReplacementOriginalBytes);
                commandLineReplacementOriginalBytes = null;
                commandLineReplacementPatchVa = 0;
                Log(
                    "Restored EAC's existing-metadata confirmation guard.");
            }
        }

        internal static void SelectCommandLineMetadataReplacementAddresses(
            string version,
            out uint guard,
            out uint accepted)
        {
            if (String.Equals(
                    version,
                    "EAC 1.8",
                    StringComparison.Ordinal))
            {
                guard = Eac18MetadataReplacementGuardVa;
                accepted = Eac18MetadataReplacementAcceptedVa;
                return;
            }
            if (String.Equals(
                    version,
                    "EAC 1.6",
                    StringComparison.Ordinal))
            {
                guard = Eac16MetadataReplacementGuardVa;
                accepted = Eac16MetadataReplacementAcceptedVa;
                return;
            }
            throw new NotSupportedException(
                "The EAC metadata replacement confirmation layout is unsupported.");
        }

        internal static bool ProvideCommandLineMetadata(CCDMetadata data, bool cdinfo, bool cover, bool lyrics)
        {
            if (commandLineInvocation == null || commandLineInvocation.Metadata == null ||
                Interlocked.CompareExchange(ref commandLineProviderCalled, 1, 0) != 0)
                return false;

            try
            {
                ApplyCommandLineMetadata(data, commandLineInvocation.Metadata, cdinfo, cover, lyrics);
                IntPtr mainWindow = ReadAbsolutePointer(layout.MainWindowGlobalVa);
                NativeMethods.PostMessageW(mainWindow, NativeMethods.WM_COMMAND,
                    new IntPtr(FinishCommandLineMetadataCommand), IntPtr.Zero);
                return true;
            }
            catch (Exception error)
            {
                commandLineError = error.Message;
                IntPtr mainWindow = ReadAbsolutePointer(layout.MainWindowGlobalVa);
                NativeMethods.PostMessageW(mainWindow, NativeMethods.WM_COMMAND,
                    new IntPtr(FailCommandLineMetadataCommand), IntPtr.Zero);
                Log("Command-line metadata application failed: " + error);
                return false;
            }
        }

        private static void ApplyCommandLineMetadata(
            CCDMetadata data,
            CommandLineMetadata metadata,
            bool cdinfo,
            bool cover,
            bool lyrics)
        {
            if (data == null)
                throw new InvalidOperationException("EAC supplied no disc metadata object.");
            if (data.NumberOfTracks != metadata.TrackCount)
                throw new InvalidOperationException("The inserted disc has " + data.NumberOfTracks +
                    " tracks, but the command-line metadata describes " + metadata.TrackCount + ".");
            if (metadata.CddbId.HasValue && Convert.ToUInt32(data.CDDBID, CultureInfo.InvariantCulture) != metadata.CddbId.Value)
                throw new InvalidOperationException("The inserted disc does not match disc.cddbId.");
            if (metadata.LeadoutPosition.HasValue && data.LeadoutPosition != metadata.LeadoutPosition.Value)
                throw new InvalidOperationException("The inserted disc does not match disc.leadoutPosition.");

            for (int i = 0; i < metadata.TrackCount; i++)
            {
                CommandLineTrackMetadata track = metadata.Tracks[i];
                uint start = data.GetTrackStartPosition(i);
                uint end = data.GetTrackEndPosition(i);
                if (metadata.TrackStartPositions != null && start != metadata.TrackStartPositions[i])
                    throw new InvalidOperationException("The inserted disc does not match disc.trackStartPositions at track " + (i + 1) + ".");
                if (track.StartPosition.HasValue && start != track.StartPosition.Value)
                    throw new InvalidOperationException("The inserted disc does not match tracks[" + i + "].startPosition.");
                if (track.EndPosition.HasValue && end != track.EndPosition.Value)
                    throw new InvalidOperationException("The inserted disc does not match tracks[" + i + "].endPosition.");
                if (track.Preemphasis.HasValue && data.GetTrackPreemphasis(i) != track.Preemphasis.Value)
                    throw new InvalidOperationException("The inserted disc does not match tracks[" + i + "].preemphasis.");
                if (track.DataTrack.HasValue && data.GetTrackDataTrack(i) != track.DataTrack.Value)
                    throw new InvalidOperationException("The inserted disc does not match tracks[" + i + "].dataTrack.");
                if (track.FourChannels.HasValue && data.GetTrack4Channels(i) != track.FourChannels.Value)
                    throw new InvalidOperationException("The inserted disc does not match tracks[" + i + "].fourChannels.");
            }

            // This provider is invoked only for an explicit command-line payload.
            // Populate the complete object even if EAC's saved online-lookup options
            // would normally request only one metadata category.
            {
                data.AlbumArtist = metadata.AlbumArtist;
                data.AlbumTitle = metadata.AlbumTitle;
                data.CDDBMusicType = metadata.CddbMusicType;
                data.Year = metadata.Year;
                data.Revision = metadata.Revision;
                data.MP3Type = metadata.Mp3Type;
                data.ExtendedDiscInformation = metadata.ExtendedDiscInformation;
                data.MP3V2Type = metadata.Mp3V2Type;
                data.FirstTrackNumber = metadata.FirstTrackNumber;
                data.AlbumInterpret = metadata.AlbumInterpret;
                data.CDNumber = metadata.CdNumber;
                data.TotalNumberOfCDs = metadata.TotalNumberOfCds;
                data.AlbumComposer = metadata.AlbumComposer;
                for (int i = 0; i < metadata.TrackCount; i++)
                {
                    CommandLineTrackMetadata track = metadata.Tracks[i];
                    data.SetTrackTitle(i, track.Title);
                    data.SetExtendedTrackInformation(i, track.ExtendedInformation);
                    data.SetTrackArtist(i, track.Artist);
                    data.SetTrackComposer(i, track.Composer);
                    data.SetTrackLyrics(i, track.Lyrics);
                }
                data.CoverImageURL = metadata.CoverImageUrl;
                data.CoverImage = metadata.CoverImage;
            }
        }

        private static void FinishCommandLineMetadata(IntPtr mainWindow, bool failed)
        {
            RestoreCommandLineMetadataReplacementBypass();
            MetadataProviderBridge.Restore(commandLineOriginalProviderIndex);
            commandLineOriginalProviderIndex = -1;
            if (failed || commandLineError != null)
            {
                ShowCommandLineError(mainWindow);
                return;
            }
            Log("Command-line metadata was applied successfully.");
            ApplyStoredAlbumMetadataValues(
                commandLineInvocation.Metadata.Label,
                commandLineInvocation.Metadata.Barcode,
                commandLineInvocation.Metadata.CatalogNumber);
            SetAlbumMetadataStoreDirty();
            PersistPendingAlbumMetadataStoreChanges();
            Log("Command-line custom album metadata was applied successfully.");
            ContinueCommandLineActions(mainWindow);
        }

        private static void ContinueCommandLineActions(IntPtr mainWindow)
        {
            try
            {
                if (!String.IsNullOrEmpty(commandLineInvocation.HtoaFilename) &&
                    Interlocked.CompareExchange(
                        ref commandLineHtoaAttempted,
                        1,
                        0) == 0 &&
                    StartHtoaWorkflow(mainWindow))
                {
                    return;
                }

                if (commandLineInvocation.RunHundredPercentLog)
                    ShowWorkflowDestinationDialog(mainWindow);
                else
                    RequestCommandLineShutdown(mainWindow);
            }
            catch (Exception error)
            {
                commandLineError = error.Message;
                Log("Command-line action sequencing failed: " + error);
                ShowCommandLineError(mainWindow);
            }
        }

        private static void ShowCommandLineError(IntPtr mainWindow)
        {
            RestoreCommandLineMetadataReplacementBypass();
            MetadataProviderBridge.Restore(commandLineOriginalProviderIndex);
            commandLineOriginalProviderIndex = -1;
            if (commandLineRequestPresent)
            {
                WriteCommandLineStandardError(
                    "EAC Enhancements: " +
                    (commandLineError ?? "Unknown command-line error."));
                RequestCommandLineShutdown(mainWindow);
                return;
            }
            NativeMethods.MessageBoxW(mainWindow,
                "The EAC Enhancements command-line request could not be completed.\r\n\r\n" +
                (commandLineError ?? "Unknown error."),
                "EAC Enhancements", NativeMethods.MB_OK | NativeMethods.MB_ICONWARNING);
        }

        internal static bool IsCommandLineWorkflow()
        {
            return commandLineInvocation != null &&
                (commandLineInvocation.RunHundredPercentLog ||
                 !String.IsNullOrEmpty(commandLineInvocation.HtoaFilename));
        }

        internal static bool IsCommandLineHtoaRequested()
        {
            return commandLineInvocation != null &&
                !String.IsNullOrEmpty(commandLineInvocation.HtoaFilename);
        }

        internal static string GetCommandLineHtoaFilename()
        {
            return IsCommandLineHtoaRequested()
                ? commandLineInvocation.HtoaFilename
                : null;
        }

        internal static bool HasCommandLineWorkflowAfterHtoa()
        {
            return commandLineInvocation != null &&
                commandLineInvocation.RunHundredPercentLog;
        }

        internal static string GetCommandLineDestination()
        {
            return IsCommandLineWorkflow()
                ? commandLineInvocation.Destination
                : null;
        }

        internal static void WriteCommandLineStandardError(string text)
        {
            try
            {
                Console.Error.WriteLine(text ?? String.Empty);
                Console.Error.Flush();
            }
            catch (Exception error)
            {
                Log("Writing command-line stderr failed: " + error.Message);
            }
        }

        private static void RequestCommandLineShutdown(IntPtr mainWindow)
        {
            if (mainWindow != IntPtr.Zero &&
                NativeMethods.IsWindow(mainWindow))
            {
                NativeMethods.PostMessageW(
                    mainWindow,
                    NativeMethods.WM_COMMAND,
                    new IntPtr((int)FinishCommandLineRunCommand),
                    IntPtr.Zero);
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int PluginGetNumPluginsDelegate(IntPtr context);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void PluginSetCurrentPluginDelegate(IntPtr context, int index);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void PluginGetTextDelegate(IntPtr context, IntPtr buffer);

        private static class MetadataProviderBridge
        {
            internal static bool TryActivate(out int originalIndex, out string error)
            {
                originalIndex = -1;
                error = null;
                try
                {
                    IntPtr context = ReadAbsolutePointer(layout.PluginHandlerContextVa);
                    if (context == IntPtr.Zero)
                        throw new InvalidOperationException("EAC's metadata plugin handler is not initialized.");
                    PluginGetNumPluginsDelegate getCount = GetDelegate<PluginGetNumPluginsDelegate>(layout.PluginGetNumPluginsPointerVa);
                    PluginSetCurrentPluginDelegate setCurrent = GetDelegate<PluginSetCurrentPluginDelegate>(layout.PluginSetCurrentPluginPointerVa);
                    PluginGetTextDelegate getGuid = GetDelegate<PluginGetTextDelegate>(layout.PluginGetPluginGuidPointerVa);
                    int count = getCount(context);
                    if (count < 1 || count > 128)
                        throw new InvalidOperationException("EAC reported an invalid metadata provider count.");

                    string originalGuid = ReadProviderText(getGuid, context);
                    int target = -1;
                    for (int i = 0; i < count; i++)
                    {
                        setCurrent(context, i);
                        string guid = ReadProviderText(getGuid, context);
                        if (GuidEquals(guid, originalGuid))
                            originalIndex = i;
                        if (GuidEquals(guid, CommandLineMetadataProviderGuid))
                            target = i;
                    }
                    if (originalIndex < 0)
                        originalIndex = FindSavedProviderIndex(context, count, setCurrent, getGuid);
                    if (target < 0)
                        throw new InvalidOperationException("The EAC Enhancements metadata provider was not loaded by EAC.");
                    setCurrent(context, target);
                    Log("Temporarily selected command-line metadata provider " + target +
                        "; prior provider " + originalIndex + ".");
                    return true;
                }
                catch (Exception exception)
                {
                    Restore(originalIndex);
                    error = exception.Message;
                    Log("Metadata provider activation failed: " + exception);
                    return false;
                }
            }

            internal static void Restore(int index)
            {
                if (index < 0 || layout == null)
                    return;
                try
                {
                    IntPtr context = ReadAbsolutePointer(layout.PluginHandlerContextVa);
                    if (context != IntPtr.Zero)
                        GetDelegate<PluginSetCurrentPluginDelegate>(layout.PluginSetCurrentPluginPointerVa)(context, index);
                    Log("Restored metadata provider " + index + ".");
                }
                catch (Exception error)
                {
                    Log("Metadata provider restoration failed: " + error);
                }
            }

            private static int FindSavedProviderIndex(
                IntPtr context,
                int count,
                PluginSetCurrentPluginDelegate setCurrent,
                PluginGetTextDelegate getGuid)
            {
                string savedGuid = null;
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(InternetOptionsKey))
                    if (key != null)
                        savedGuid = Convert.ToString(key.GetValue("MetadataPluginUsed"), CultureInfo.InvariantCulture);
                if (String.IsNullOrEmpty(savedGuid))
                    return 0;
                for (int i = 0; i < count; i++)
                {
                    setCurrent(context, i);
                    if (GuidEquals(ReadProviderText(getGuid, context), savedGuid))
                        return i;
                }
                return 0;
            }

            private static T GetDelegate<T>(uint pointerStaticVa) where T : class
            {
                IntPtr pointer = ReadAbsolutePointer(pointerStaticVa);
                if (pointer == IntPtr.Zero)
                    throw new InvalidOperationException("An EAC metadata helper function is unavailable.");
                return (T)(object)Marshal.GetDelegateForFunctionPointer(pointer, typeof(T));
            }

            private static string ReadProviderText(PluginGetTextDelegate getter, IntPtr context)
            {
                IntPtr buffer = Marshal.AllocHGlobal(2048);
                try
                {
                    for (int i = 0; i < 2048; i++)
                        Marshal.WriteByte(buffer, i, 0);
                    getter(context, buffer);
                    string ansi = Marshal.PtrToStringAnsi(buffer) ?? String.Empty;
                    string unicode = Marshal.PtrToStringUni(buffer) ?? String.Empty;
                    return LooksLikeGuidOrName(unicode) && unicode.Length > ansi.Length / 2 ? unicode : ansi;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }

            private static bool LooksLikeGuidOrName(string value)
            {
                if (String.IsNullOrEmpty(value))
                    return false;
                for (int i = 0; i < value.Length; i++)
                    if (Char.IsControl(value[i]) && !Char.IsWhiteSpace(value[i]))
                        return false;
                return true;
            }

            private static bool GuidEquals(string left, string right)
            {
                Guid a;
                Guid b;
                return Guid.TryParse((left ?? String.Empty).Trim(), out a) &&
                       Guid.TryParse((right ?? String.Empty).Trim(), out b) && a == b;
            }
        }
    }
}
