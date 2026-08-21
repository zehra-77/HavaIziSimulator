using HavaIziSimulator.Mcp.Abstractions;
using HavaIziSimulator.Mcp.Radar.CreateTracks;
using HavaIziSimulator.Mcp.Radar.DropTracks;
using HavaIziSimulator.Mcp.Radar.ExecuteActions;
using HavaIziSimulator.Mcp.Radar.Heartbeat;
using HavaIziSimulator.Mcp.Radar.QueryActiveTracks;
using HavaIziSimulator.Mcp.Radar.UpdateClassification;
using HavaIziSimulator.Mcp.Radar.UpdateDiagnosis;
using HavaIziSimulator.Mcp.Radar.UpdateTracks;
using HavaIziSimulator.Mcp.Time.ScheduleAction;
using HavaIziSimulator.Mcp.Spatial.CreateTracks;
using HavaIziSimulator.Mcp.Spatial.QueryTracks;
using HavaIziSimulator.Mcp.Spatial.Services;

namespace HavaIziSimulator.Mcp.Registry;

public static class McpToolCatalog
{
    public static IReadOnlyList<IMcpTool> CreateDefault()
    {
        var geometry = new SpatialGeometryService(new NominatimGeocodingClient());
        IMcpTool[] actionTools =
        [
            new CreateTracksTool(),
            new DropTracksTool(),
            new UpdateDiagnosisTool(),
            new UpdateClassificationTool(),
            new UpdateTracksTool(),
            new SendHeartbeatTool(),
            new CreateSpatialTracksTool(geometry),
            new ScheduleRadarActionTool()
        ];

        return
        [
            new ExecuteRadarActionsTool(actionTools),
            new QueryActiveTracksTool(),
            new QuerySpatialTracksTool(geometry),
            .. actionTools
        ];
    }
}
