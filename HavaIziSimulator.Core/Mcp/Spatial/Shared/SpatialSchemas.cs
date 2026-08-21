namespace HavaIziSimulator.Mcp.Spatial.Shared;

public static class SpatialSchemas
{
    public const string Scope = """
        {"type":"object","properties":{
          "type":{"type":"string","enum":["REGION","POINT","RADIUS"]},
          "placeName":{"anyOf":[{"type":"string"},{"type":"null"}]},
          "radiusKm":{"description":"RADIUS kapsamının merkezden kilometre yarıçapı.","anyOf":[{"type":"number","exclusiveMinimum":0},{"type":"null"}]}
        },"required":["type"],"additionalProperties":false}
        """;

    public const string Filter = """
        {"anyOf":[{"type":"object","properties":{
          "trackIds":{"anyOf":[{"type":"array","items":{"type":"integer","minimum":1,"maximum":10000}},{"type":"null"}]},
          "teshis":{"anyOf":[{"type":"string","enum":["BILINMEYEN","DOST","DUSMAN","TARAFSIZ"]},{"type":"null"}]},
          "tasnif":{"anyOf":[{"type":"string","enum":["BILINMIYOR","UCAK","DONERKANAT","FUZE","IHA"]},{"type":"null"}]},
          "yonelim":{"anyOf":[{"type":"string","enum":["KUZEY","GUNEY","DOGU","BATI"]},{"type":"null"}]},
          "minHiz":{"anyOf":[{"type":"integer","minimum":0,"maximum":1000},{"type":"null"}]},
          "maxHiz":{"anyOf":[{"type":"integer","minimum":0,"maximum":1000},{"type":"null"}]},
          "minYukseklik":{"anyOf":[{"type":"integer","minimum":0,"maximum":5000},{"type":"null"}]},
          "maxYukseklik":{"anyOf":[{"type":"integer","minimum":0,"maximum":5000},{"type":"null"}]}
        },"additionalProperties":false},{"type":"null"}]}
        """;

    public static string Query(bool spatial) => spatial
        ? $$"""
          {"type":"object","properties":{
            "operation":{"type":"string","enum":["count","list","summary"]},
            "scope":{{Scope}},
            "filter":{{Filter}},
            "sortBy":{"anyOf":[{"type":"string","enum":["trackId","hiz","yukseklik"]},{"type":"null"}]},
            "descending":{"anyOf":[{"type":"boolean"},{"type":"null"}]},
            "limit":{"anyOf":[{"type":"integer","minimum":1,"maximum":1000},{"type":"null"}]}
          },"required":["operation","scope"],"additionalProperties":false}
          """
        : $$"""
          {"type":"object","properties":{
            "operation":{"type":"string","enum":["count","list","summary"]},
            "filter":{{Filter}},
            "sortBy":{"anyOf":[{"type":"string","enum":["trackId","hiz","yukseklik"]},{"type":"null"}]},
            "descending":{"anyOf":[{"type":"boolean"},{"type":"null"}]},
            "limit":{"anyOf":[{"type":"integer","minimum":1,"maximum":1000},{"type":"null"}]}
          },"required":["operation"],"additionalProperties":false}
          """;

    public const string Groups = """
        {"type":"array","minItems":1,"maxItems":20,"items":{
          "type":"object","properties":{
            "count":{"type":"integer","minimum":1,"maximum":100},
            "trackIds":{"anyOf":[{"type":"array","items":{"type":"integer","minimum":1,"maximum":10000}},{"type":"null"}]},
            "hiz":{"anyOf":[{"type":"integer","minimum":0,"maximum":1000},{"type":"null"}]},
            "yukseklik":{"anyOf":[{"type":"integer","minimum":0,"maximum":5000},{"type":"null"}]},
            "yonelim":{"anyOf":[{"type":"string","enum":["KUZEY","GUNEY","DOGU","BATI"]},{"type":"null"}]},
            "teshis":{"anyOf":[{"type":"string","enum":["BILINMEYEN","DOST","DUSMAN","TARAFSIZ"]},{"type":"null"}]},
            "tasnif":{"anyOf":[{"type":"string","enum":["BILINMIYOR","UCAK","DONERKANAT","FUZE","IHA"]},{"type":"null"}]}
          },"required":["count"],"additionalProperties":false}}
        """;

    public static string Create => $$"""
        {"type":"object","properties":{
          "scope":{{Scope}},
          "groups":{{Groups}}
        },"required":["scope","groups"],"additionalProperties":false}
        """;
}
