local Decoder = {}
local jsonNull = {}

local function codepointToUtf8(value)
    if value <= 0x7f then
        return string.char(value)
    elseif value <= 0x7ff then
        return string.char(0xc0 + math.floor(value / 0x40), 0x80 + (value % 0x40))
    elseif value <= 0xffff then
        return string.char(
            0xe0 + math.floor(value / 0x1000),
            0x80 + (math.floor(value / 0x40) % 0x40),
            0x80 + (value % 0x40))
    end
    return string.char(
        0xf0 + math.floor(value / 0x40000),
        0x80 + (math.floor(value / 0x1000) % 0x40),
        0x80 + (math.floor(value / 0x40) % 0x40),
        0x80 + (value % 0x40))
end

function Decoder.decode(text)
    if type(text) ~= "string" then
        error("JSON input must be a string")
    end

    local position = 1
    local length = #text
    local parseValue

    local function fail(message)
        error(message .. " at byte " .. tostring(position))
    end

    local function skipWhitespace()
        while position <= length do
            local byte = string.byte(text, position)
            if byte ~= 0x20 and byte ~= 0x09 and byte ~= 0x0a and byte ~= 0x0d then
                break
            end
            position = position + 1
        end
    end

    local function readHexCodepoint()
        local digits = string.sub(text, position, position + 3)
        if #digits ~= 4 or not string.match(digits, "^%x%x%x%x$") then
            fail("Invalid unicode escape")
        end
        position = position + 4
        return tonumber(digits, 16)
    end

    local function parseString()
        position = position + 1
        local parts = {}
        local start = position
        while position <= length do
            local byte = string.byte(text, position)
            if byte == 0x22 then
                parts[#parts + 1] = string.sub(text, start, position - 1)
                position = position + 1
                return table.concat(parts)
            elseif byte == 0x5c then
                parts[#parts + 1] = string.sub(text, start, position - 1)
                position = position + 1
                local escape = string.sub(text, position, position)
                local replacements = {
                    ['"'] = '"', ['\\'] = '\\', ['/'] = '/',
                    ['b'] = string.char(8), ['f'] = string.char(12),
                    ['n'] = string.char(10), ['r'] = string.char(13),
                    ['t'] = string.char(9)
                }
                if escape == "u" then
                    position = position + 1
                    local codepoint = readHexCodepoint()
                    if codepoint >= 0xd800 and codepoint <= 0xdbff then
                        if string.sub(text, position, position + 1) ~= "\\u" then
                            fail("Missing low surrogate")
                        end
                        position = position + 2
                        local low = readHexCodepoint()
                        if low < 0xdc00 or low > 0xdfff then
                            fail("Invalid low surrogate")
                        end
                        codepoint = 0x10000 + (codepoint - 0xd800) * 0x400 + (low - 0xdc00)
                    elseif codepoint >= 0xdc00 and codepoint <= 0xdfff then
                        fail("Unexpected low surrogate")
                    end
                    parts[#parts + 1] = codepointToUtf8(codepoint)
                    start = position
                elseif replacements[escape] then
                    parts[#parts + 1] = replacements[escape]
                    position = position + 1
                    start = position
                else
                    fail("Invalid string escape")
                end
            elseif byte < 0x20 then
                fail("Control character in string")
            else
                position = position + 1
            end
        end
        fail("Unterminated string")
    end

    local function parseNumber()
        local start = position
        if string.sub(text, position, position) == "-" then
            position = position + 1
        end
        if string.sub(text, position, position) == "0" then
            position = position + 1
        else
            if not string.match(string.sub(text, position, position), "%d") then
                fail("Invalid number")
            end
            while string.match(string.sub(text, position, position), "%d") do
                position = position + 1
            end
        end
        if string.sub(text, position, position) == "." then
            position = position + 1
            if not string.match(string.sub(text, position, position), "%d") then
                fail("Invalid fraction")
            end
            while string.match(string.sub(text, position, position), "%d") do
                position = position + 1
            end
        end
        local marker = string.sub(text, position, position)
        if marker == "e" or marker == "E" then
            position = position + 1
            local sign = string.sub(text, position, position)
            if sign == "+" or sign == "-" then
                position = position + 1
            end
            if not string.match(string.sub(text, position, position), "%d") then
                fail("Invalid exponent")
            end
            while string.match(string.sub(text, position, position), "%d") do
                position = position + 1
            end
        end
        local value = tonumber(string.sub(text, start, position - 1))
        if value == nil then
            fail("Invalid number")
        end
        return value
    end

    local function parseArray()
        position = position + 1
        skipWhitespace()
        local result = setmetatable({}, { __jsonType = "array" })
        if string.sub(text, position, position) == "]" then
            position = position + 1
            return result
        end
        while true do
            result[#result + 1] = parseValue()
            skipWhitespace()
            local token = string.sub(text, position, position)
            if token == "]" then
                position = position + 1
                return result
            elseif token ~= "," then
                fail("Expected comma or closing bracket")
            end
            position = position + 1
            skipWhitespace()
        end
    end

    local function parseObject()
        position = position + 1
        skipWhitespace()
        local result = setmetatable({}, { __jsonType = "object" })
        if string.sub(text, position, position) == "}" then
            position = position + 1
            return result
        end
        while true do
            if string.sub(text, position, position) ~= '"' then
                fail("Expected object key")
            end
            local key = parseString()
            skipWhitespace()
            if string.sub(text, position, position) ~= ":" then
                fail("Expected colon")
            end
            position = position + 1
            skipWhitespace()
            local value = parseValue()
            if value ~= jsonNull then
                result[key] = value
            end
            skipWhitespace()
            local token = string.sub(text, position, position)
            if token == "}" then
                position = position + 1
                return result
            elseif token ~= "," then
                fail("Expected comma or closing brace")
            end
            position = position + 1
            skipWhitespace()
        end
    end

    function parseValue()
        skipWhitespace()
        local token = string.sub(text, position, position)
        if token == '"' then
            return parseString()
        elseif token == "{" then
            return parseObject()
        elseif token == "[" then
            return parseArray()
        elseif token == "-" or string.match(token, "%d") then
            return parseNumber()
        elseif string.sub(text, position, position + 3) == "true" then
            position = position + 4
            return true
        elseif string.sub(text, position, position + 4) == "false" then
            position = position + 5
            return false
        elseif string.sub(text, position, position + 3) == "null" then
            position = position + 4
            return jsonNull
        end
        fail("Unexpected token")
    end

    local result = parseValue()
    skipWhitespace()
    if position <= length then
        fail("Trailing JSON content")
    end
    return result
end

Decoder.null = jsonNull
DeadlineWidgetJson = Decoder
return Decoder
