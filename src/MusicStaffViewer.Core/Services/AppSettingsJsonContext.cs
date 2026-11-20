using System.Text.Json.Serialization;
using Com.Example.MusicStaffViewer.Core.Models;

namespace Com.Example.MusicStaffViewer.Core.Services;

[JsonSerializable(typeof(AppSettings))]
internal sealed partial class AppSettingsJsonContext : JsonSerializerContext;
