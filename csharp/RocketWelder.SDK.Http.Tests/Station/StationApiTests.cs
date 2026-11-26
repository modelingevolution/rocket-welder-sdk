using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using RocketWelder.SDK.Http;
using RocketWelder.SDK.Http.Programs;
using RocketWelder.SDK.Http.Station;
using RocketWelder.SDK.Http.Tests.Programs;

namespace RocketWelder.SDK.Http.Tests.Station;

/// <summary>
/// epic-029 feature-011 design.md §12.4 / §12.6 — station Programming defaults: the §12.4 example round-trips, and
/// <see cref="IStationApi"/> hits the right verb and route with no <c>If-Match</c>.
/// </summary>
public class StationApiTests
{
    private const string Key = "Adaptation.PathTolerance";
    private const string Route = "/api/station/programming-defaults";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    // §12.4 response
    private const string StationDefaultsJson = """
        {"values":[
          {"key":"Adaptation.OffsetTolerance","kind":"Adaptation","blockTypes":["Adapt","AdaptManually","AutoAdapt","FullAutoAdapt"],
           "control":"ToleranceBandField","stored":null,"effective":"10:10","layer":"builtIn","warning":null},
          {"key":"Adaptation.PathTolerance","kind":"Adaptation","blockTypes":["Adapt","AdaptManually","AutoAdapt","FullAutoAdapt"],
           "control":"ToleranceBandField","stored":"1:3","effective":"1:3","layer":"station","warning":null}]}
        """;

    [Fact]
    public void StationDefaults_RoundTripsTheStationExample()
    {
        var dto = JsonSerializer.Deserialize<StationDefaults>(StationDefaultsJson, Web)!;

        dto.Values.Should().HaveCount(2);
        var path = dto.Values[1];
        path.Key.Should().Be(Key);
        path.Kind.Should().Be("Adaptation");
        path.BlockTypes.Should().Equal("Adapt", "AdaptManually", "AutoAdapt", "FullAutoAdapt");
        path.Control.Should().Be("ToleranceBandField");
        path.Stored.Should().Be("1:3");
        path.Effective.Should().Be("1:3");
        path.Layer.Should().Be("station");
        path.Warning.Should().BeNull();
        dto.Values[0].Stored.Should().BeNull();
        dto.Values[0].Layer.Should().Be("builtIn");

        var written = JsonNode.Parse(JsonSerializer.Serialize(dto, Web));
        JsonNode.DeepEquals(written, JsonNode.Parse(StationDefaultsJson)).Should().BeTrue(written!.ToJsonString());
    }

    [Fact]
    public async Task GetProgrammingDefaultsAsync_GETsTheRoute()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, StationDefaultsJson);

        var result = await Station(handler).GetProgrammingDefaultsAsync();

        handler.Method.Should().Be(HttpMethod.Get);
        handler.RequestUri!.AbsolutePath.Should().Be(Route);
        handler.IfMatchPresent.Should().BeFalse();
        result.Values.Should().HaveCount(2);
    }

    [Fact]
    public async Task SetProgrammingDefaultAsync_PUTsTheValueBody_WithoutIfMatch()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, StationDefaultsJson);

        var result = await Station(handler).SetProgrammingDefaultAsync(Key, "1:3");

        handler.Method.Should().Be(HttpMethod.Put);
        handler.RequestUri!.AbsolutePath.Should().Be($"{Route}/{Key}");
        handler.IfMatchPresent.Should().BeFalse();
        JsonNode.Parse(handler.Body!)!["value"]!.GetValue<string>().Should().Be("1:3");
        result.Values[1].Effective.Should().Be("1:3");
    }

    [Fact]
    public async Task ACellValuesSourceAndStationOnlyFlag_AreRead_AndOlderAnswersWithoutThemStillRead()
    {
        // A station's cell value (rw2 F-21): Cell.HoldPosture carries its source and stationOnly; a block property carries neither.
        const string json = """
            {"values":[
              {"key":"Adaptation.PathTolerance","kind":"Adaptation","blockTypes":["Adapt"],"control":"ToleranceBandField","stored":null,"effective":"10:10","layer":"builtIn","warning":null},
              {"key":"Cell.HoldPosture","kind":"Cell","blockTypes":[],"control":"JointsField","stored":"26.1,-108.9,111.9,-94.6,-89.9,0","effective":"26.1,-108.9,111.9,-94.6,-89.9,0","layer":"station","warning":null,"source":"the cell's home; measured 2026-11-26","stationOnly":true}
            ]}
            """;
        var handler = new RecordingHandler(HttpStatusCode.OK, json);

        var result = await Station(handler).GetProgrammingDefaultsAsync();

        result.Values[0].Source.Should().BeNull();
        result.Values[0].StationOnly.Should().BeFalse();
        result.Values[1].Source.Should().Be("the cell's home; measured 2026-11-26");
        result.Values[1].StationOnly.Should().BeTrue();
    }

    [Fact]
    public void ASetValueRequestWithoutASource_SerializesAsBefore_WithOneTheSourceIsSent()
    {
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        JsonSerializer.Serialize(new SetValueRequest("1:3"), web).Should().Be("""{"value":"1:3"}""", "every existing request is unchanged");
        JsonNode.Parse(JsonSerializer.Serialize(new SetValueRequest("1,2,3,4,5,6") { Source = "measured" }, web))!["source"]!.GetValue<string>()
            .Should().Be("measured");
    }

    [Fact]
    public async Task SetProgrammingDefaultAsync_EscapesReservedCharactersInTheKey()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, StationDefaultsJson);

        await Station(handler).SetProgrammingDefaultAsync("Adaptation/Path Tolerance", "1:3");

        handler.RequestUri!.AbsoluteUri.Should().EndWith($"{Route}/Adaptation%2FPath%20Tolerance");
    }

    [Fact]
    public async Task ClearProgrammingDefaultAsync_DELETEsTheKey()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, StationDefaultsJson);

        var result = await Station(handler).ClearProgrammingDefaultAsync(Key);

        handler.Method.Should().Be(HttpMethod.Delete);
        handler.RequestUri!.AbsolutePath.Should().Be($"{Route}/{Key}");
        handler.IfMatchPresent.Should().BeFalse();
        result.Values.Should().HaveCount(2);
    }

    [Fact]
    public async Task SetProgrammingDefaultAsync_On400_ThrowsValueRefused_WithTheBodySentence()
    {
        const string refusal = "accept must not exceed review — write it as accept:review, e.g. 1.2:3.5.";
        var handler = new RecordingHandler(HttpStatusCode.BadRequest, JsonSerializer.Serialize(refusal));

        var act = () => Station(handler).SetProgrammingDefaultAsync(Key, "3.5:1.2");

        (await act.Should().ThrowAsync<ValueRefusedException>()).Which.Reason.Should().Be(refusal);
    }

    [Fact]
    public async Task SetProgrammingDefaultAsync_On404_ThrowsHttpRequestException_CarryingTheSentence()
    {
        const string sentence = "No programming default 'Adaptation.Foo'.";
        var handler = new RecordingHandler(HttpStatusCode.NotFound, JsonSerializer.Serialize(sentence));

        var act = () => Station(handler).SetProgrammingDefaultAsync("Adaptation.Foo", "1:2");

        var ex = (await act.Should().ThrowExactlyAsync<HttpRequestException>()).Which;
        ex.StatusCode.Should().Be(HttpStatusCode.NotFound);
        ex.Message.Should().EndWith($" {sentence}").And.NotContain("\"");
    }

    private static IStationApi Station(RecordingHandler handler) =>
        new RocketWelderClient(RecordingHandler.Client(handler)).Station;
}
