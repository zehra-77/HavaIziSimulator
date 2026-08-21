namespace HavaIziSimulator.Mcp.Radar.Shared;

public static class RadarSchemas
{
    public const string Create = """
        {"type":"object","properties":{
          "groups":{"type":"array","minItems":1,"maxItems":20,"items":{
            "type":"object","properties":{
              "count":{"type":"integer","minimum":1,"maximum":100},
              "trackIds":{"anyOf":[{"type":"array","items":{"type":"integer","minimum":1,"maximum":10000}},{"type":"null"}]},
              "hiz":{"anyOf":[{"type":"integer","minimum":0,"maximum":1000},{"type":"null"}]},
              "yukseklik":{"anyOf":[{"type":"integer","minimum":0,"maximum":5000},{"type":"null"}]},
              "yonelim":{"anyOf":[{"type":"string","enum":["KUZEY","GUNEY","DOGU","BATI"]},{"type":"null"}]},
              "teshis":{"anyOf":[{"type":"string","enum":["BILINMEYEN","DOST","DUSMAN","TARAFSIZ"]},{"type":"null"}]},
              "tasnif":{"anyOf":[{"type":"string","enum":["BILINMIYOR","UCAK","DONERKANAT","FUZE","IHA"]},{"type":"null"}]},
              "enlem":{"anyOf":[{"type":"number","minimum":-90,"maximum":90},{"type":"null"}]},
              "boylam":{"anyOf":[{"type":"number","minimum":-180,"maximum":180},{"type":"null"}]}
            },"required":["count"],"additionalProperties":false
          }}
        },"required":["groups"],"additionalProperties":false}
        """;

    public const string Update = """
        {"type":"object","properties":{
          "field":{"type":"string","enum":["all","trackId","hiz","yukseklik","yonelim","teshis","tasnif"]},
          "operator":{"type":"string","enum":["eq","lt","lte","gt","gte"]},
          "value":{"anyOf":[{"type":"number"},{"type":"string"}]},
          "limit":{"anyOf":[{"type":"integer","minimum":1},{"type":"null"}]},
          "random":{"anyOf":[{"type":"boolean"},{"type":"null"}]},
          "hiz":{"anyOf":[{"type":"integer","minimum":0,"maximum":1000},{"type":"null"}]},
          "yukseklik":{"anyOf":[{"type":"integer","minimum":0,"maximum":5000},{"type":"null"}]},
          "yonelim":{"anyOf":[{"type":"string","enum":["KUZEY","GUNEY","DOGU","BATI"]},{"type":"null"}]},
          "enlem":{"anyOf":[{"type":"number","minimum":-90,"maximum":90},{"type":"null"}]},
          "boylam":{"anyOf":[{"type":"number","minimum":-180,"maximum":180},{"type":"null"}]}
        },"required":["field","operator","value"],"additionalProperties":false}
        """;

    public static string Filter(string actionProperty, string values) => $$"""
        {"type":"object","properties":{
          "field":{"type":"string","enum":["all","trackId","hiz","yukseklik","yonelim","teshis","tasnif"]},
          "operator":{"type":"string","enum":["eq","lt","lte","gt","gte"]},
          "value":{"anyOf":[{"type":"number"},{"type":"string"}]},
          "limit":{"anyOf":[{"type":"integer","minimum":1},{"type":"null"}]},
          "random":{"anyOf":[{"type":"boolean"},{"type":"null"}]},
          "{{actionProperty}}":{"type":"string","enum":{{values}}}
        },"required":["field","operator","value","{{actionProperty}}"],"additionalProperties":false}
        """;
}
