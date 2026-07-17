using System;
using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("Interop.HelperFunctionsLib compile-time reference")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: ImportedFromTypeLib("HelperFunctionsLib")]
[assembly: TypeLibVersion(1, 0)]
[assembly: Guid("DD6BDE89-9B08-447B-B314-061E3980607F")]

// This assembly mirrors only EAC's public COM contracts used by the plugin.
// It is generated for CI compilation and is never distributed with releases;
// EAC supplies the real Interop.HelperFunctionsLib.dll at runtime.
namespace HelperFunctionsLib
{
    [ComImport]
    [Guid("41EAAABE-7B44-4B3E-BD9F-1703698706A6")]
    [TypeLibType((TypeLibTypeFlags)4288)]
    public interface IAudioDataTransfer
    {
        [DispId(1)]
        void StartNewSession(IMetadataLookup data, string driveName, int offset, bool accurateRipOffset, int mode);
        [DispId(2)]
        void StartNewTransfer(int startPosition, int length, bool test);
        [DispId(3)]
        void ShowOptions();
        [DispId(4)]
        string GetAudioTransferPluginName();
        [DispId(5)]
        string GetAudioTransferPluginGuid();
        [DispId(6)]
        void TransferAudioData(Array audioData);
        [DispId(7)]
        void TransferFinished();
        [DispId(8)]
        string EndOfSession();
        [DispId(9)]
        void SuspiciousPosition();
    }

    [ComImport]
    [Guid("DADF63E7-B7D6-4E97-8C60-570AA5A156EB")]
    [TypeLibType((TypeLibTypeFlags)4288)]
    public interface IMetadataLookup
    {
        [DispId(1001)] uint CDDBID { get; }
        [DispId(1002)] uint CDPlayerINI { get; }
        [DispId(1003)] uint NumberOfTracks { get; }
        [DispId(1004)] uint LeadoutPosition { get; }
        [DispId(1005)] uint GetTrackStartPosition(int track);
        [DispId(1006)] string AlbumArtist { get; }
        [DispId(1007)] string AlbumTitle { get; }
        [DispId(1008)] int CDDBMusicType { get; }
        [DispId(1009)] int Year { get; }
        [DispId(1010)] int Revision { get; }
        [DispId(1011)] int MP3Type { get; }
        [DispId(1029)] string GetTrackTitle(int track);
        [DispId(1013)] string ExtendedDiscInformation { get; }
        [DispId(1037)] string GetExtendedTrackInformation(int track);
        [DispId(1015)] string MP3V2Type { get; }
        [DispId(1016)] Array CoverImage { get; }
        [DispId(1019)] string EmailUser { get; }
        [DispId(1020)] string EmailHost { get; }
        [DispId(1021)] int FirstTrackNumber { get; }
        [DispId(1038)] string GetTrackArtist(int track);
        [DispId(1035)] string GetTrackComposer(int track);
        [DispId(1024)] string AlbumInterpret { get; }
        [DispId(1025)] int CDNumber { get; }
        [DispId(1026)] int TotalNumberOfCDs { get; }
        [DispId(1027)] string AlbumComposer { get; }
        [DispId(1030)] string GetTrackLyrics(int track);
        [DispId(1040)] string CoverImageURL { get; }
        [DispId(1041)] int PhysicalFirstTrackNumber { get; }
        [DispId(1042)] bool GetTrackPreemphasis(int track);
        [DispId(1043)] bool GetTrackDataTrack(int track);
        [DispId(1044)] bool GetTrack4Channels(int track);
        [DispId(1045)] uint GetTrackEndPosition(int track);
        [DispId(1046)] string HostVersion { get; }
    }

    [ComImport]
    [Guid("39547008-605B-41EF-AB1C-DC4FAAA4E4BB")]
    [TypeLibType((TypeLibTypeFlags)4288)]
    public interface ICDMetadata
    {
        [DispId(1001)] uint CDDBID { get; }
        [DispId(1002)] uint CDPlayerINI { get; }
        [DispId(1003)] uint NumberOfTracks { get; }
        [DispId(1004)] uint LeadoutPosition { get; }
        [DispId(1005)] uint GetTrackStartPosition(int track);
        [DispId(1006)] string AlbumArtist { get; set; }
        [DispId(1007)] string AlbumTitle { get; set; }
        [DispId(1008)] int CDDBMusicType { get; set; }
        [DispId(1009)] int Year { get; set; }
        [DispId(1010)] int Revision { get; set; }
        [DispId(1011)] int MP3Type { get; set; }
        [DispId(1029)] string GetTrackTitle(int track);
        [DispId(1029)] void SetTrackTitle(int track, string value);
        [DispId(1013)] string ExtendedDiscInformation { get; set; }
        [DispId(1037)] string GetExtendedTrackInformation(int track);
        [DispId(1037)] void SetExtendedTrackInformation(int track, string value);
        [DispId(1015)] string MP3V2Type { get; set; }
        [DispId(1016)] Array CoverImage { get; set; }
        [DispId(1019)] string EmailUser { get; }
        [DispId(1020)] string EmailHost { get; }
        [DispId(1021)] int FirstTrackNumber { get; set; }
        [DispId(1038)] string GetTrackArtist(int track);
        [DispId(1038)] void SetTrackArtist(int track, string value);
        [DispId(1035)] string GetTrackComposer(int track);
        [DispId(1035)] void SetTrackComposer(int track, string value);
        [DispId(1024)] string AlbumInterpret { get; set; }
        [DispId(1025)] int CDNumber { get; set; }
        [DispId(1026)] int TotalNumberOfCDs { get; set; }
        [DispId(1027)] string AlbumComposer { get; set; }
        [DispId(1030)] string GetTrackLyrics(int track);
        [DispId(1030)] void SetTrackLyrics(int track, string value);
        [DispId(1040)] string CoverImageURL { get; set; }
        [DispId(1041)] int PhysicalFirstTrackNumber { get; }
        [DispId(1042)] bool GetTrackPreemphasis(int track);
        [DispId(1043)] bool GetTrackDataTrack(int track);
        [DispId(1044)] bool GetTrack4Channels(int track);
        [DispId(1045)] uint GetTrackEndPosition(int track);
        [DispId(1046)] string HostVersion { get; }
    }

    [ComImport]
    [Guid("39547008-605B-41EF-AB1C-DC4FAAA4E4BB")]
    [CoClass(typeof(CCDMetadataClass))]
    public interface CCDMetadata : ICDMetadata
    {
    }

    [ComImport]
    [Guid("2C00FD29-09C5-43CE-946D-27B626C7F414")]
    [TypeLibType(TypeLibTypeFlags.FCanCreate)]
    [ClassInterface(ClassInterfaceType.None)]
    public class CCDMetadataClass
    {
    }

    [ComImport]
    [Guid("F11F7ACC-17EF-4192-A7C8-4955AF2ADE00")]
    [TypeLibType((TypeLibTypeFlags)4288)]
    public interface IMetadataRetriever
    {
        [DispId(1)]
        bool GetCDInformation(CCDMetadata data, bool cdInfo, bool cover, bool lyrics);
        [DispId(2)]
        void ShowOptions();
        [DispId(3)]
        string GetPluginName();
        [DispId(4)]
        string GetPluginGuid();
        [DispId(5)]
        Array GetPluginLogo();
        [DispId(6)]
        bool SupportsMetadataRetrieval();
        [DispId(7)]
        bool SupportsCoverRetrieval();
        [DispId(8)]
        bool SupportsLyricsRetrieval();
        [DispId(9)]
        bool SupportsMetadataSubmission();
        [DispId(10)]
        bool SubmitCDInformation(IMetadataLookup data);
    }
}
