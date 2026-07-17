using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
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
        internal CommandLineMetadata Metadata;

        internal static CommandLineInvocation Parse(string[] arguments)
        {
            CommandLineInvocation result = new CommandLineInvocation();
            string encodedMetadata = null;

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
            }

            if (encodedMetadata != null)
                result.Metadata = D1MetadataCodec.Decode(encodedMetadata);
            if (result.RunHundredPercentLog && result.Metadata == null)
                throw new FormatException("--eace-100-log requires --eace-metadata=d1.<payload>.");
            return result;
        }

        internal bool HasWork
        {
            get { return Metadata != null; }
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
        private const uint BeginCommandLineMetadataCommand = 0xA316;
        private const uint FinishCommandLineMetadataCommand = 0xA317;
        private const uint FailCommandLineMetadataCommand = 0xA318;
        private const int MetadataLookupCommand = 525;
        private const string InternetOptionsKey = @"Software\AWSoftware\EACU\Internet Options";
        private static CommandLineInvocation commandLineInvocation;
        private static string commandLineError;
        private static int commandLineStartPosted;
        private static int commandLineProviderCalled;
        private static int commandLineOriginalProviderIndex = -1;

        private static void InitializeCommandLine()
        {
            try
            {
                commandLineInvocation = CommandLineInvocation.Parse(Environment.GetCommandLineArgs());
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

            Thread thread = new Thread(delegate()
            {
                for (int i = 0; i < 600 && NativeMethods.IsWindow(mainWindow); i++)
                {
                    if (commandLineError != null || IsReferenceRipCommandEnabled(mainWindow))
                    {
                        if (Interlocked.CompareExchange(ref commandLineStartPosted, 1, 0) == 0)
                            NativeMethods.PostMessageW(mainWindow, NativeMethods.WM_COMMAND,
                                new IntPtr(BeginCommandLineMetadataCommand), IntPtr.Zero);
                        return;
                    }
                    Thread.Sleep(200);
                }
                commandLineError = "No ready audio CD was detected within two minutes.";
                NativeMethods.PostMessageW(mainWindow, NativeMethods.WM_COMMAND,
                    new IntPtr(BeginCommandLineMetadataCommand), IntPtr.Zero);
            });
            thread.IsBackground = true;
            thread.Name = "EAC Enhancements command-line starter";
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
            return state != UInt32.MaxValue && (state & (NativeMethods.MF_DISABLED | NativeMethods.MF_GRAYED)) == 0;
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
            if (!NativeMethods.PostMessageW(mainWindow, NativeMethods.WM_COMMAND,
                new IntPtr(MetadataLookupCommand), IntPtr.Zero))
            {
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
            MetadataProviderBridge.Restore(commandLineOriginalProviderIndex);
            commandLineOriginalProviderIndex = -1;
            if (failed || commandLineError != null)
            {
                ShowCommandLineError(mainWindow);
                return;
            }
            Log("Command-line metadata was applied successfully.");
            if (commandLineInvocation.RunHundredPercentLog)
                ShowWorkflowDestinationDialog(mainWindow);
        }

        private static void ShowCommandLineError(IntPtr mainWindow)
        {
            MetadataProviderBridge.Restore(commandLineOriginalProviderIndex);
            commandLineOriginalProviderIndex = -1;
            NativeMethods.MessageBoxW(mainWindow,
                "The EAC Enhancements command-line request could not be completed.\r\n\r\n" +
                (commandLineError ?? "Unknown error."),
                "EAC Enhancements", NativeMethods.MB_OK | NativeMethods.MB_ICONWARNING);
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
