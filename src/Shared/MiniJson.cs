using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Shared
{
    /// <summary>
    /// Исключение разбора JSON: сообщение объясняет проблему и позицию в тексте (M9, ADR-011).
    /// </summary>
    public sealed class JsonParseException : Exception
    {
        /// <summary>Создаёт исключение.</summary>
        /// <param name="message">Что именно не так.</param>
        /// <param name="position">Позиция символа в исходном тексте (с нуля).</param>
        public JsonParseException(string message, int position)
            : base(message + " (позиция " + position + ")")
        {
            Position = position;
        }

        /// <summary>Позиция символа, на котором разбор остановился.</summary>
        public int Position { get; }
    }

    /// <summary>
    /// Минимальный JSON-парсер для конфигов (M9, ADR-011): без NuGet и без System.Text.Json, потому что
    /// сборка <c>Shared</c> грузится в Unity, где System.Text.Json недоступен (см. Discovered Issues).
    /// Поддерживает объекты, массивы, строки, числа, true/false/null, `//` и `/* */` комментарии,
    /// висячие запятые — ровно то, что допускал прежний загрузчик на System.Text.Json.
    /// </summary>
    public static class MiniJson
    {
        /// <summary>Разбирает JSON-документ в дерево: <c>Dictionary&lt;string, object?&gt;</c>, <c>List&lt;object?&gt;</c>, string, double, bool, null.</summary>
        /// <param name="text">Текст JSON.</param>
        /// <returns>Корень документа.</returns>
        /// <exception cref="ArgumentNullException">Если <paramref name="text"/> равен <c>null</c>.</exception>
        /// <exception cref="JsonParseException">Если текст не является корректным JSON.</exception>
        public static object? Parse(string text)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            var parser = new Parser(text);
            var value = parser.ParseDocument();
            return value;
        }

        private sealed class Parser
        {
            private const int MaxDepth = 64;

            private readonly string _text;
            private int _index;

            public Parser(string text)
            {
                _text = text;
            }

            public object? ParseDocument()
            {
                SkipWhitespace();
                var value = ParseValue(0);
                SkipWhitespace();
                if (!AtEnd)
                {
                    throw Error("лишние символы после JSON-документа");
                }

                return value;
            }

            private bool AtEnd => _index >= _text.Length;

            private char Current => _text[_index];

            private object? ParseValue(int depth)
            {
                if (depth > MaxDepth)
                {
                    throw Error("слишком глубокая вложенность JSON");
                }

                if (AtEnd)
                {
                    throw Error("неожиданный конец JSON");
                }

                var c = Current;
                switch (c)
                {
                    case '{':
                        return ParseObject(depth);
                    case '[':
                        return ParseArray(depth);
                    case '"':
                        return ParseString();
                    case 't':
                        ExpectLiteral("true");
                        return true;
                    case 'f':
                        ExpectLiteral("false");
                        return false;
                    case 'n':
                        ExpectLiteral("null");
                        return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9'))
                        {
                            return ParseNumber();
                        }

                        throw Error("неожиданный символ '" + c + "'");
                }
            }

            private Dictionary<string, object?> ParseObject(int depth)
            {
                var result = new Dictionary<string, object?>(StringComparer.Ordinal);
                _index++; // '{'
                SkipWhitespace();

                if (AtEnd)
                {
                    throw Error("незакрытый объект");
                }

                if (Current == '}')
                {
                    _index++;
                    return result;
                }

                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd || Current != '"')
                    {
                        throw Error("ожидалось имя поля в кавычках");
                    }

                    var key = ParseString();
                    SkipWhitespace();
                    if (AtEnd || Current != ':')
                    {
                        throw Error("ожидалось ':' после имени поля");
                    }

                    _index++;
                    SkipWhitespace();
                    result[key] = ParseValue(depth + 1);
                    SkipWhitespace();

                    if (AtEnd)
                    {
                        throw Error("незакрытый объект");
                    }

                    if (Current == ',')
                    {
                        _index++;
                        SkipWhitespace();
                        if (!AtEnd && Current == '}')
                        {
                            _index++;
                            return result;
                        }

                        continue;
                    }

                    if (Current == '}')
                    {
                        _index++;
                        return result;
                    }

                    throw Error("ожидалось ',' или '}' в объекте");
                }
            }

            private List<object?> ParseArray(int depth)
            {
                var result = new List<object?>();
                _index++; // '['
                SkipWhitespace();

                if (AtEnd)
                {
                    throw Error("незакрытый массив");
                }

                if (Current == ']')
                {
                    _index++;
                    return result;
                }

                while (true)
                {
                    SkipWhitespace();
                    result.Add(ParseValue(depth + 1));
                    SkipWhitespace();

                    if (AtEnd)
                    {
                        throw Error("незакрытый массив");
                    }

                    if (Current == ',')
                    {
                        _index++;
                        SkipWhitespace();
                        if (!AtEnd && Current == ']')
                        {
                            _index++;
                            return result;
                        }

                        continue;
                    }

                    if (Current == ']')
                    {
                        _index++;
                        return result;
                    }

                    throw Error("ожидалось ',' или ']' в массиве");
                }
            }

            private string ParseString()
            {
                _index++; // '"'
                var builder = new StringBuilder();

                while (!AtEnd)
                {
                    var c = Current;
                    if (c == '"')
                    {
                        _index++;
                        return builder.ToString();
                    }

                    if (c == '\\')
                    {
                        _index++;
                        builder.Append(ParseEscape());
                        continue;
                    }

                    if (c < 0x20)
                    {
                        throw Error("управляющий символ в строке");
                    }

                    builder.Append(c);
                    _index++;
                }

                throw Error("незакрытая строка");
            }

            private char ParseEscape()
            {
                if (AtEnd)
                {
                    throw Error("незавершённая escape-последовательность");
                }

                var c = Current;
                _index++;
                switch (c)
                {
                    case '"': return '"';
                    case '\\': return '\\';
                    case '/': return '/';
                    case 'b': return '\b';
                    case 'f': return '\f';
                    case 'n': return '\n';
                    case 'r': return '\r';
                    case 't': return '\t';
                    case 'u': return ParseUnicodeEscape();
                    default:
                        throw Error("неизвестная escape-последовательность '\\" + c + "'");
                }
            }

            private char ParseUnicodeEscape()
            {
                if (_index + 4 > _text.Length)
                {
                    throw Error("обрезанная \\u-последовательность");
                }

                var code = 0;
                for (var i = 0; i < 4; i++)
                {
                    var digit = HexValue(_text[_index + i]);
                    if (digit < 0)
                    {
                        throw Error("неверная \\u-последовательность");
                    }

                    code = (code << 4) | digit;
                }

                _index += 4;
                return (char)code;
            }

            private static int HexValue(char c)
            {
                if (c >= '0' && c <= '9')
                {
                    return c - '0';
                }

                if (c >= 'a' && c <= 'f')
                {
                    return c - 'a' + 10;
                }

                if (c >= 'A' && c <= 'F')
                {
                    return c - 'A' + 10;
                }

                return -1;
            }

            private double ParseNumber()
            {
                var start = _index;

                if (Current == '-')
                {
                    _index++;
                }

                if (AtEnd || Current < '0' || Current > '9')
                {
                    throw Error("ожидалась цифра в числе");
                }

                while (!AtEnd && Current >= '0' && Current <= '9')
                {
                    _index++;
                }

                if (!AtEnd && Current == '.')
                {
                    _index++;
                    if (AtEnd || Current < '0' || Current > '9')
                    {
                        throw Error("ожидались цифры после '.'");
                    }

                    while (!AtEnd && Current >= '0' && Current <= '9')
                    {
                        _index++;
                    }
                }

                if (!AtEnd && (Current == 'e' || Current == 'E'))
                {
                    _index++;
                    if (!AtEnd && (Current == '+' || Current == '-'))
                    {
                        _index++;
                    }

                    if (AtEnd || Current < '0' || Current > '9')
                    {
                        throw Error("ожидались цифры в экспоненте");
                    }

                    while (!AtEnd && Current >= '0' && Current <= '9')
                    {
                        _index++;
                    }
                }

                var text = _text.Substring(start, _index - start);
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                    || double.IsNaN(value)
                    || double.IsInfinity(value))
                {
                    throw Error("некорректное число '" + text + "'");
                }

                return value;
            }

            private void ExpectLiteral(string literal)
            {
                if (_index + literal.Length > _text.Length
                    || string.CompareOrdinal(_text, _index, literal, 0, literal.Length) != 0)
                {
                    throw Error("ожидалось '" + literal + "'");
                }

                _index += literal.Length;
            }

            private void SkipWhitespace()
            {
                while (!AtEnd)
                {
                    var c = Current;
                    if (c == ' ' || c == '\t' || c == '\r' || c == '\n')
                    {
                        _index++;
                        continue;
                    }

                    if (c == '/' && _index + 1 < _text.Length)
                    {
                        var next = _text[_index + 1];
                        if (next == '/')
                        {
                            _index += 2;
                            while (!AtEnd && Current != '\n')
                            {
                                _index++;
                            }

                            continue;
                        }

                        if (next == '*')
                        {
                            _index += 2;
                            while (_index + 1 < _text.Length && !(Current == '*' && _text[_index + 1] == '/'))
                            {
                                _index++;
                            }

                            if (_index + 1 >= _text.Length)
                            {
                                throw Error("незакрытый комментарий /* */");
                            }

                            _index += 2;
                            continue;
                        }
                    }

                    return;
                }
            }

            private JsonParseException Error(string message)
            {
                return new JsonParseException(message, _index);
            }
        }
    }
}
