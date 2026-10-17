local Renderer = {}
local decoder = DeadlineWidgetJson
local currentPage = 1

local function ensureDecoder()
    if decoder then
        return decoder
    end
    if not SKIN then
        error("JSON decoder is unavailable")
    end
    local resourcePath = SKIN:GetVariable("@")
    decoder = dofile(resourcePath .. "json.lua")
    return decoder
end

local function formatLocalEpoch(epoch, localOffset)
    local localTime
    if localOffset then
        localTime = os.date("!*t", epoch + localOffset)
    else
        localTime = os.date("*t", epoch)
    end
    return string.format(
        "%04d-%02d-%02d %02d:%02d",
        localTime.year,
        localTime.month,
        localTime.day,
        localTime.hour,
        localTime.min)
end

local function daysInMonth(year, month)
    if month == 2 then
        if year % 400 == 0 or (year % 4 == 0 and year % 100 ~= 0) then
            return 29
        end
        return 28
    elseif month == 4 or month == 6 or month == 9 or month == 11 then
        return 30
    end
    return 31
end

local function parseIsoDate(value, localOffsetOverride)
    if type(value) ~= "string" then
        return nil
    end
    local year, month, day, hour, minute, second, suffix = string.match(
        value,
        "^(%d%d%d%d)%-(%d%d)%-(%d%d)T(%d%d):(%d%d):(%d%d)(.*)$")
    if not year then
        return nil
    end
    year, month, day = tonumber(year), tonumber(month), tonumber(day)
    hour, minute, second = tonumber(hour), tonumber(minute), tonumber(second)
    if month < 1 or month > 12 or day < 1 or day > daysInMonth(year, month) or
       hour > 23 or minute > 59 or second > 59 then
        return nil
    end
    local fraction, zone = string.match(suffix, "^(%.%d+)(.*)$")
    if fraction then
        suffix = zone
    end
    local ok, localEpoch = pcall(os.time, {
        year = year,
        month = month,
        day = day,
        hour = hour,
        min = minute,
        sec = second
    })
    if not ok or not localEpoch then
        return nil
    end
    local normalized = os.date("*t", localEpoch)
    if normalized.year ~= year or normalized.month ~= month or normalized.day ~= day or
       normalized.hour ~= hour or normalized.min ~= minute or normalized.sec ~= second then
        return nil
    end
    local utcComponents = os.date("!*t", localEpoch)
    utcComponents.isdst = nil
    local systemOffset = os.difftime(localEpoch, os.time(utcComponents))
    local displayOffset = tonumber(localOffsetOverride)
    if suffix == "" then
        return localEpoch, formatLocalEpoch(localEpoch, displayOffset)
    end
    if suffix == "Z" or suffix == "z" then
        local dueEpoch = localEpoch + systemOffset
        return dueEpoch, formatLocalEpoch(dueEpoch, displayOffset)
    end
    local sign, zoneHour, zoneMinute = string.match(suffix, "^([+-])(%d%d):(%d%d)$")
    if not sign then
        return nil
    end
    zoneHour, zoneMinute = tonumber(zoneHour), tonumber(zoneMinute)
    if zoneHour > 23 or zoneMinute > 59 then
        return nil
    end
    local zoneOffset = zoneHour * 3600 + zoneMinute * 60
    if sign == "-" then
        zoneOffset = -zoneOffset
    end
    local dueEpoch = localEpoch + systemOffset - zoneOffset
    return dueEpoch, formatLocalEpoch(dueEpoch, displayOffset)
end

local function bridgeError(status, labels)
    if status == 2 then
        return labels.bridgeCommandError
    elseif status == 3 then
        return labels.bridgeMissing
    elseif status == 4 then
        return labels.bridgeLaunchError
    end
    return nil
end

local function classify(seconds)
    if seconds < 0 then
        return "OverdueColor"
    elseif seconds > 10 * 86400 then
        return "NeutralColor"
    elseif seconds > 3 * 86400 then
        return "WarningColor"
    end
    return "CriticalColor"
end

local function countdown(seconds, labels)
    if seconds < 0 then
        return labels.overdue
    elseif seconds < 86400 then
        local hours = math.floor(seconds / 3600)
        local minutes = math.floor((seconds % 3600) / 60)
        return tostring(hours) .. " " .. labels.hours .. " " ..
            tostring(minutes) .. " " .. labels.minutes
    end
    return tostring(math.ceil(seconds / 86400)) .. " " .. labels.days
end

local function validateDocument(document, localOffsetOverride)
    if type(document) ~= "table" or document.version ~= 1 or
       type(document.generatedAt) ~= "string" or type(document.items) ~= "table" then
        return nil
    end
    local itemsMetadata = getmetatable(document.items)
    if not itemsMetadata or itemsMetadata.__jsonType ~= "array" then
        return nil
    end
    local items = {}
    for index = 1, #document.items do
        local item = document.items[index]
        if type(item) ~= "table" or type(item.id) ~= "string" or
           type(item.title) ~= "string" or type(item.dueAt) ~= "string" or
           (item.isCompleted ~= nil and type(item.isCompleted) ~= "boolean") then
            return nil
        end
        local dueEpoch, dueText = parseIsoDate(item.dueAt, localOffsetOverride)
        if not dueEpoch then
            return nil
        end
        if not item.isCompleted then
            items[#items + 1] = {
                id = item.id,
                title = item.title,
                dueEpoch = dueEpoch,
                dueText = dueText
            }
        end
    end
    return items
end

local function sortItems(items, nowEpoch)
    table.sort(items, function(left, right)
        local leftOverdue = left.dueEpoch < nowEpoch
        local rightOverdue = right.dueEpoch < nowEpoch
        if leftOverdue ~= rightOverdue then
            return not leftOverdue
        end
        if left.dueEpoch ~= right.dueEpoch then
            return left.dueEpoch < right.dueEpoch
        end
        if left.title ~= right.title then
            return left.title < right.title
        end
        return left.id < right.id
    end)
end

local function errorResult(status, footer)
    return {
        status = status,
        total = 0,
        page = 1,
        pages = 1,
        footer = footer,
        rows = {}
    }
end

local function applyBridgeError(result, bridgeStatus, labels)
    local bridgeMessage = bridgeError(tonumber(bridgeStatus) or 0, labels)
    if bridgeMessage then
        result.status = "bridge-error"
        result.footer = bridgeMessage
    end
    return result
end

local function stripUtf8Bom(text)
    if string.sub(text, 1, 3) == "\239\187\191" then
        return string.sub(text, 4)
    end
    return text
end

local function render(snapshotText, nowEpoch, requestedPage, labels, bridgeStatus, testLocalOffsetSeconds)
    local ok, document = pcall(function()
        return ensureDecoder().decode(stripUtf8Bom(snapshotText))
    end)
    if not ok then
        return applyBridgeError(errorResult("error", labels.error), bridgeStatus, labels)
    end
    local items = validateDocument(document, testLocalOffsetSeconds)
    if not items then
        return applyBridgeError(errorResult("error", labels.error), bridgeStatus, labels)
    end
    sortItems(items, nowEpoch)

    local total = #items
    if total == 0 then
        return applyBridgeError(errorResult("empty", labels.empty), bridgeStatus, labels)
    end
    local pages = math.max(1, math.ceil(total / 5))
    local page = math.floor(tonumber(requestedPage) or 1)
    page = math.max(1, math.min(page, pages))
    local rows = {}
    local first = (page - 1) * 5 + 1
    local last = math.min(first + 4, total)
    for index = first, last do
        local item = items[index]
        local remaining = item.dueEpoch - nowEpoch
        rows[#rows + 1] = {
            title = item.title,
            due = item.dueText,
            countdown = countdown(remaining, labels),
            color = classify(remaining)
        }
    end
    return applyBridgeError({
        status = "ok",
        total = total,
        page = page,
        pages = pages,
        footer = labels.totalPrefix .. " " .. tostring(total) .. " " .. labels.itemUnit ..
            " " .. labels.separator .. " " .. labels.pagePrefix .. " " .. tostring(page) ..
            "/" .. tostring(pages) .. " " .. labels.pageUnit,
        rows = rows
    }, bridgeStatus, labels)
end

local function labelsFromSkin()
    return {
        days = SKIN:GetVariable("LabelDays"),
        hours = SKIN:GetVariable("LabelHours"),
        minutes = SKIN:GetVariable("LabelMinutes"),
        overdue = SKIN:GetVariable("LabelOverdue"),
        empty = SKIN:GetVariable("LabelEmpty"),
        error = SKIN:GetVariable("LabelError"),
        bridgeCommandError = SKIN:GetVariable("LabelBridgeCommandError"),
        bridgeMissing = SKIN:GetVariable("LabelBridgeMissing"),
        bridgeLaunchError = SKIN:GetVariable("LabelBridgeLaunchError"),
        totalPrefix = SKIN:GetVariable("LabelTotalPrefix"),
        itemUnit = SKIN:GetVariable("LabelItemUnit"),
        pagePrefix = SKIN:GetVariable("LabelPagePrefix"),
        pageUnit = SKIN:GetVariable("LabelPageUnit"),
        separator = SKIN:GetVariable("LabelSeparator")
    }
end

local function readSnapshot()
    if SKIN:GetVariable("TestMode") ~= "1" then
        local ok, contents = pcall(function()
            return SKIN:GetMeasure("MeasureBridge"):GetStringValue()
        end)
        if ok and type(contents) == "string" and contents ~= "" then
            return contents
        end
        return nil
    end
    local path = SKIN:GetVariable("SnapshotPath")
    local file = io.open(path, "rb")
    if not file then
        return nil
    end
    local contents = file:read("*a")
    file:close()
    return contents
end

local function writeTestResult(path, result)
    if not path or path == "" then
        return
    end
    local file = io.open(path, "wb")
    if not file then
        return
    end
    file:write("status=" .. result.status .. "\n")
    file:write("total=" .. tostring(result.total) .. "\n")
    file:write("page=" .. tostring(result.page) .. "\n")
    file:write("pages=" .. tostring(result.pages) .. "\n")
    file:write("footer=" .. result.footer .. "\n")
    for index = 1, #result.rows do
        local row = result.rows[index]
        file:write("row" .. tostring(index) .. ".title=" .. row.title .. "\n")
        file:write("row" .. tostring(index) .. ".due=" .. row.due .. "\n")
        file:write("row" .. tostring(index) .. ".countdown=" .. row.countdown .. "\n")
        file:write("row" .. tostring(index) .. ".color=" .. row.color .. "\n")
    end
    file:close()
end

local function bridgeStatusFromSkin()
    local ok, value = pcall(function()
        return SKIN:GetMeasure("MeasureBridge"):GetValue()
    end)
    if ok then
        return tonumber(value) or 0
    end
    return 0
end

local function applyResult(result)
    for index = 1, 5 do
        local group = "Row" .. tostring(index)
        local row = result.rows[index]
        if row then
            SKIN:Bang("!SetVariable", group .. "Title", row.title)
            SKIN:Bang("!SetVariable", group .. "Due", row.due)
            SKIN:Bang("!SetVariable", group .. "Countdown", row.countdown)
            SKIN:Bang("!SetVariable", group .. "Color", "#" .. row.color .. "#")
            SKIN:Bang("!ShowMeterGroup", group)
        else
            SKIN:Bang("!HideMeterGroup", group)
        end
    end
    SKIN:Bang("!SetVariable", "StatusText", result.footer)
    SKIN:Bang("!UpdateMeter", "*")
    SKIN:Bang("!Redraw")
end

local function renderSkin()
    local snapshot = readSnapshot()
    local labels = labelsFromSkin()
    local testMode = SKIN:GetVariable("TestMode") == "1"
    local nowEpoch = os.time()
    if testMode then
        nowEpoch = tonumber(SKIN:GetVariable("TestNowEpoch")) or nowEpoch
    end
    local result
    local bridgeStatus = bridgeStatusFromSkin()
    if snapshot then
        result = render(snapshot, nowEpoch, currentPage, labels, bridgeStatus)
    else
        result = applyBridgeError(errorResult("error", labels.error), bridgeStatus, labels)
    end
    if result.total > 0 then
        currentPage = result.page
    elseif result.status == "empty" then
        currentPage = 1
    end
    applyResult(result)
    if testMode then
        writeTestResult(SKIN:GetVariable("TestResultPath"), result)
    end
end

function Initialize()
    ensureDecoder()
    if SKIN:GetVariable("TestMode") == "1" then
        currentPage = tonumber(SKIN:GetVariable("TestPage")) or 1
    end
end

function Update()
    renderSkin()
    return 0
end

function Page(delta)
    currentPage = math.max(1, currentPage + (tonumber(delta) or 0))
    renderSkin()
end

Renderer.TestRender = render
DeadlineWidgetV2 = Renderer
return Renderer
