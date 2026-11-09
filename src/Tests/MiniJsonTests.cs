using System;
using System.Collections.Generic;
using Shared;
using Xunit;

namespace Tests
{
    /// <summary>M9: минимальный JSON-парсер из `Shared` (ADR-011) — грамматика и понятные ошибки.</summary>
    public sealed class MiniJsonTests
    {
        [Fact]
        public void Parse_ObjectWithMixedTypes()
        {
            var root = MiniJson.Parse(@"{ ""id"": 1, ""moveSpeed"": 4.5, ""name"": ""hero"", ""alive"": true, ""target"": null }");

            var dto = Assert.IsType<Dictionary<string, object?>>(root);
            Assert.Equal(1d, Assert.IsType<double>(dto["id"]));
            Assert.Equal(4.5d, Assert.IsType<double>(dto["moveSpeed"]));
            Assert.Equal("hero", Assert.IsType<string>(dto["name"]));
            Assert.True(Assert.IsType<bool>(dto["alive"]));
            Assert.Null(dto["target"]);
        }

        [Fact]
        public void Parse_ArrayOfObjectsAndNesting()
        {
            var root = MiniJson.Parse(@"[
  { ""id"": 1, ""tags"": [""a"", ""b""], ""nested"": { ""x"": -2 } },
  { ""id"": 2 }
]");

            var list = Assert.IsType<List<object?>>(root);
            Assert.Equal(2, list.Count);
            var first = Assert.IsType<Dictionary<string, object?>>(list[0]);
            Assert.Equal(2, Assert.IsType<List<object?>>(first["tags"]).Count);
            Assert.Equal(-2d, Assert.IsType<double>(Assert.IsType<Dictionary<string, object?>>(first["nested"])["x"]));
        }

        [Fact]
        public void Parse_CommentsAndTrailingCommas_AreAllowed()
        {
            var root = MiniJson.Parse(@"[
  // строка-комментарий
  { ""id"": 1, /* поле */ ""maxHp"": 100, },
]");

            var dto = Assert.IsType<Dictionary<string, object?>>(Assert.IsType<List<object?>>(root)[0]);
            Assert.Equal(100d, Assert.IsType<double>(dto["maxHp"]));
        }

        [Fact]
        public void Parse_StringEscapes()
        {
            var root = MiniJson.Parse("\"a\\n\\t\\\"b\\\" \\u0041\\uD83D\\uDE00\"");
            var text = Assert.IsType<string>(root);

            Assert.Contains("\n\t\"b\"", text);
            Assert.Contains("A", text);
            Assert.Contains("\U0001F600", text);
        }

        [Fact]
        public void Parse_EmptyObjectAndArray()
        {
            Assert.Empty(Assert.IsType<Dictionary<string, object?>>(MiniJson.Parse("{}")));
            Assert.Empty(Assert.IsType<List<object?>>(MiniJson.Parse("[]")));
        }

        [Fact]
        public void Parse_NullArgument_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => MiniJson.Parse(null!));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("{ not json")]
        [InlineData("{ \"id\": }")]
        [InlineData("{ \"id\": 1 } trailing")]
        [InlineData("\"unterminated")]
        [InlineData("{ \"id\": 1 ")]
        [InlineData("[1, 2")]
        [InlineData("{ /* unterminated }")]
        [InlineData("\"bad \\q escape\"")]
        [InlineData("\"bad \\u00G1 escape\"")]
        [InlineData("01x")]
        public void Parse_InvalidJson_ThrowsWithPosition(string text)
        {
            var exception = Assert.Throws<JsonParseException>(() => MiniJson.Parse(text));

            Assert.Contains("позиция", exception.Message);
            Assert.True(exception.Position >= 0);
        }
    }
}
