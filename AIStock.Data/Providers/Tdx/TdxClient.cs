using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Net.Sockets;
using System.Text;

namespace AIStock.Data.Providers.Tdx;

/// <summary>
/// 通达信行情 TCP 客户端
/// </summary>
public sealed class TdxClient : IAsyncDisposable
{
    const ushort TypeConnect = 0x000D;
    const ushort TypeCount = 0x044E;
    const ushort TypeCode = 0x0450;
    const ushort TypeQuote = 0x053E;
    const ushort TypeMinute = 0x051D;
    const ushort TypeKline = 0x052D;
    const ushort TypeHistoryMinute = 0x0FB4;
    const ushort TypeMinuteTrade = 0x0FC5;
    const ushort TypeHistoryMinuteTrade = 0x0FB5;
    const ushort TypeCallAuction = 0x056A;

    static readonly Encoding GbkEncoding = CreateGbkEncoding();

    readonly string _host;
    readonly int _port;
    readonly TcpClient _tcp = new();
    NetworkStream? _stream;
    uint _msgId;

    public TdxClient(string host, int port)
    {
        _host = host;
        _port = port;
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await _tcp.ConnectAsync(_host, _port, cancellationToken);
        _stream = _tcp.GetStream();
        await SendFrameAsync(TypeConnect, [0x01], cancellationToken);
        _ = await ReadResponseAsync(cancellationToken);
    }

    public async Task<int> GetCodeCountAsync(TdxExchange exchange, CancellationToken cancellationToken = default)
    {
        var data = new byte[] { exchange.ToByte(), 0x00, 0x75, 0xc7, 0x33, 0x01 };
        var response = await RequestAsync(TypeCount, data, cancellationToken);
        EnsureLength(response.Data, 2);
        return BinaryPrimitives.ReadUInt16LittleEndian(response.Data);
    }

    public async Task<TdxCodeResponse> GetCodesAsync(TdxExchange exchange, ushort start = 0, CancellationToken cancellationToken = default)
    {
        using var ms = new MemoryStream();
        ms.WriteByte(exchange.ToByte());
        ms.WriteByte(0x00);
        WriteUInt16LE(ms, start);

        var response = await RequestAsync(TypeCode, ms.ToArray(), cancellationToken);
        return DecodeCodes(response.Data);
    }

    public async Task<IReadOnlyList<TdxQuote>> GetQuotesAsync(IEnumerable<string> codes, CancellationToken cancellationToken = default)
    {
        var normalized = codes.Select(TdxCode.AddPrefix).ToArray();
        using var ms = new MemoryStream();
        ms.Write([0x05, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]);
        WriteUInt16LE(ms, checked((ushort)normalized.Length));

        foreach (var code in normalized)
        {
            var (exchange, number) = TdxCode.Decode(code);
            ms.WriteByte(exchange.ToByte());
            ms.Write(Encoding.ASCII.GetBytes(number));
        }

        var response = await RequestAsync(TypeQuote, ms.ToArray(), cancellationToken);
        return DecodeQuotes(response.Data);
    }

    public async Task<TdxMinuteResponse> GetMinuteAsync(string code, CancellationToken cancellationToken = default)
    {
        return await GetHistoryMinuteAsync(DateTime.Today.ToString("yyyyMMdd", CultureInfo.InvariantCulture), code, cancellationToken);
    }

    public async Task<TdxMinuteResponse> GetHistoryMinuteAsync(string date, string code, CancellationToken cancellationToken = default)
    {
        var (exchange, number) = TdxCode.Decode(TdxCode.AddPrefix(code));
        using var ms = new MemoryStream();
        WriteUInt32LE(ms, ParseDateNumber(date));
        ms.WriteByte(exchange.ToByte());
        ms.Write(Encoding.ASCII.GetBytes(number));

        var response = await RequestAsync(TypeHistoryMinute, ms.ToArray(), cancellationToken);
        return DecodeHistoryMinute(response.Data);
    }

    public async Task<TdxKlineResponse> GetKlinesAsync(string code, TdxKlineType type, ushort start = 0, ushort count = 800, CancellationToken cancellationToken = default)
    {
        if (count > 800)
            throw new ArgumentOutOfRangeException(nameof(count), "单次 K 线请求最多 800 条。");

        var fullCode = TdxCode.AddPrefix(code);
        var (exchange, number) = TdxCode.Decode(fullCode);
        using var ms = new MemoryStream();
        ms.WriteByte(exchange.ToByte());
        ms.WriteByte(0x00);
        ms.Write(Encoding.ASCII.GetBytes(number));
        ms.WriteByte((byte)type);
        ms.WriteByte(0x00);
        ms.WriteByte(0x01);
        ms.WriteByte(0x00);
        WriteUInt16LE(ms, start);
        WriteUInt16LE(ms, count);
        ms.Write(new byte[10]);

        var response = await RequestAsync(TypeKline, ms.ToArray(), cancellationToken);
        return DecodeKlines(response.Data, type, TdxCode.IsIndex(fullCode));
    }

    public async Task<TdxTradeResponse> GetMinuteTradesAsync(string code, ushort start = 0, ushort count = 1800, CancellationToken cancellationToken = default)
    {
        var fullCode = TdxCode.AddPrefix(code);
        var (exchange, number) = TdxCode.Decode(fullCode);
        using var ms = new MemoryStream();
        ms.WriteByte(exchange.ToByte());
        ms.WriteByte(0x00);
        ms.Write(Encoding.ASCII.GetBytes(number));
        WriteUInt16LE(ms, start);
        WriteUInt16LE(ms, count);

        var response = await RequestAsync(TypeMinuteTrade, ms.ToArray(), cancellationToken);
        return DecodeTrades(response.Data, DateTime.Today, fullCode, hasOrderCount: true, skipUnknownHeader: false);
    }

    public async Task<TdxTradeResponse> GetHistoryMinuteTradesAsync(string date, string code, ushort start = 0, ushort count = 2000, CancellationToken cancellationToken = default)
    {
        var fullCode = TdxCode.AddPrefix(code);
        var (exchange, number) = TdxCode.Decode(fullCode);
        using var ms = new MemoryStream();
        WriteUInt32LE(ms, ParseDateNumber(date));
        ms.WriteByte(exchange.ToByte());
        ms.WriteByte(0x00);
        ms.Write(Encoding.ASCII.GetBytes(number));
        WriteUInt16LE(ms, start);
        WriteUInt16LE(ms, count);

        var response = await RequestAsync(TypeHistoryMinuteTrade, ms.ToArray(), cancellationToken);
        var day = DateTime.ParseExact(date, "yyyyMMdd", CultureInfo.InvariantCulture);
        return DecodeTrades(response.Data, day, fullCode, hasOrderCount: false, skipUnknownHeader: true);
    }

    public async Task<TdxCallAuctionResponse> GetCallAuctionAsync(string code, CancellationToken cancellationToken = default)
    {
        var (exchange, number) = TdxCode.Decode(TdxCode.AddPrefix(code));
        using var ms = new MemoryStream();
        ms.WriteByte(exchange.ToByte());
        ms.WriteByte(0x00);
        ms.Write(Encoding.ASCII.GetBytes(number));
        ms.Write([0x00, 0x00, 0x00, 0x00, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xf4, 0x01, 0x00, 0x00]);

        var response = await RequestAsync(TypeCallAuction, ms.ToArray(), cancellationToken);
        return DecodeCallAuction(response.Data);
    }

    async Task<TdxResponse> RequestAsync(ushort type, byte[] data, CancellationToken cancellationToken)
    {
        await SendFrameAsync(type, data, cancellationToken);
        return await ReadResponseAsync(cancellationToken);
    }

    async Task SendFrameAsync(ushort type, byte[] data, CancellationToken cancellationToken)
    {
        var stream = RequireStream();
        var msgId = ++_msgId;
        var len = checked((ushort)(data.Length + 2));

        var frame = new byte[12 + data.Length];
        frame[0] = 0x0C;
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(1, 4), msgId);
        frame[5] = 0x01;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(6, 2), len);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(8, 2), len);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(10, 2), type);
        data.CopyTo(frame.AsSpan(12));

        await stream.WriteAsync(frame, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    async Task<TdxResponse> ReadResponseAsync(CancellationToken cancellationToken)
    {
        var stream = RequireStream();
        var prefix = new byte[4];

        while (true)
        {
            await ReadExactAsync(stream, prefix, cancellationToken);
            if (prefix[0] == 0xB1 && prefix[1] == 0xCB && prefix[2] == 0x74 && prefix[3] == 0x00)
                break;
        }

        var header = new byte[12];
        await ReadExactAsync(stream, header, cancellationToken);

        var control = header[0];
        var msgId = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(1, 4));
        var unknown = header[5];
        var type = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(6, 2));
        var zipLen = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(8, 2));
        var rawLen = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(10, 2));

        var body = new byte[zipLen];
        await ReadExactAsync(stream, body, cancellationToken);
        var data = zipLen == rawLen ? body : ZlibDecompress(body);

        if (data.Length != rawLen)
            throw new InvalidDataException($"响应解压长度不匹配，预期 {rawLen}，实际 {data.Length}。");

        return new TdxResponse(control, msgId, unknown, type, data);
    }

    static TdxCodeResponse DecodeCodes(byte[] data)
    {
        EnsureLength(data, 2);
        var count = BinaryPrimitives.ReadUInt16LittleEndian(data);
        var items = new List<TdxCodeInfo>(count);
        var offset = 2;

        for (var i = 0; i < count && offset + 29 <= data.Length; i++, offset += 29)
        {
            var code = Encoding.ASCII.GetString(data, offset, 6).TrimEnd('\0');
            var multiple = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset + 6, 2));
            var name = DecodeGbk(data.AsSpan(offset + 8, 8));
            var decimalPlaces = (sbyte)data[offset + 20];
            var lastPrice = DecodeCompressedNumber(BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 21, 4)));
            items.Add(new TdxCodeInfo(code, name, multiple, decimalPlaces, lastPrice));
        }

        return new TdxCodeResponse(count, items);
    }

    static IReadOnlyList<TdxQuote> DecodeQuotes(byte[] data)
    {
        EnsureLength(data, 4);
        var offset = 2;
        var count = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));
        offset += 2;
        var items = new List<TdxQuote>(count);

        for (var i = 0; i < count && offset < data.Length; i++)
        {
            EnsureLength(data, offset + 9);
            var exchange = (TdxExchange)data[offset++];
            var code = Encoding.ASCII.GetString(data, offset, 6).TrimEnd('\0');
            offset += 6;
            offset += 2;

            var k = DecodeK(data, ref offset);
            var serverTime = ReadVarInt(data, ref offset).ToString(CultureInfo.InvariantCulture);
            _ = ReadVarInt(data, ref offset);
            var totalHand = ReadVarInt(data, ref offset);
            var currentVolume = ReadVarInt(data, ref offset);
            var amount = DecodeCompressedNumber(BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4)));
            offset += 4;
            var insideDish = ReadVarInt(data, ref offset);
            var outerDisc = ReadVarInt(data, ref offset);
            _ = ReadVarInt(data, ref offset);
            _ = ReadVarInt(data, ref offset);

            var buy = new List<TdxPriceLevel>(5);
            var sell = new List<TdxPriceLevel>(5);
            for (var level = 0; level < 5; level++)
            {
                var buyPrice = ReadVarInt(data, ref offset) * 10L + k.CloseRaw;
                var sellPrice = ReadVarInt(data, ref offset) * 10L + k.CloseRaw;
                var buyVolume = ReadVarInt(data, ref offset);
                var sellVolume = ReadVarInt(data, ref offset);
                buy.Add(new TdxPriceLevel(ToPrice(buyPrice), buyVolume));
                sell.Add(new TdxPriceLevel(ToPrice(sellPrice), sellVolume));
            }

            if (offset + 2 <= data.Length) offset += 2;
            for (var n = 0; n < 4 && offset < data.Length; n++) _ = ReadVarInt(data, ref offset);
            if (offset + 4 <= data.Length) offset += 4;

            items.Add(new TdxQuote(exchange, code, serverTime, ToPrice(k.LastRaw), ToPrice(k.OpenRaw),
                ToPrice(k.HighRaw), ToPrice(k.LowRaw), ToPrice(k.CloseRaw),
                totalHand, currentVolume, amount, insideDish, outerDisc, buy, sell));
        }

        return items;
    }

    static TdxMinuteResponse DecodeHistoryMinute(byte[] data)
    {
        EnsureLength(data, 6);
        var count = BinaryPrimitives.ReadUInt16LittleEndian(data);
        var offset = 6;
        var items = new List<TdxMinuteItem>(count);
        var lastPrice = 0L;
        var t = new TimeOnly(9, 30);

        for (var i = 0; i < count && offset < data.Length; i++)
        {
            var priceDiff = ReadVarInt(data, ref offset);
            _ = ReadVarInt(data, ref offset);
            lastPrice += priceDiff;
            var volume = ReadVarInt(data, ref offset);
            if (i == 120) t = t.Add(TimeSpan.FromMinutes(90));
            items.Add(new TdxMinuteItem(t.Add(TimeSpan.FromMinutes(i + 1)).ToString("HH:mm", CultureInfo.InvariantCulture), ToPrice(lastPrice * 10), volume));
        }

        return new TdxMinuteResponse(count, items);
    }

    static TdxKlineResponse DecodeKlines(byte[] data, TdxKlineType type, bool isIndex)
    {
        EnsureLength(data, 2);
        var count = BinaryPrimitives.ReadUInt16LittleEndian(data);
        var offset = 2;
        var items = new List<TdxKlineItem>(count);
        var last = 0L;

        for (var i = 0; i < count && offset + 4 <= data.Length; i++)
        {
            var time = DecodeKlineTime(data.AsSpan(offset, 4), type);
            offset += 4;

            var openDiff = ReadVarInt(data, ref offset);
            var closeDiff = ReadVarInt(data, ref offset);
            var highDiff = ReadVarInt(data, ref offset);
            var lowDiff = ReadVarInt(data, ref offset);

            var open = last + openDiff;
            var close = last + openDiff + closeDiff;
            var high = last + openDiff + highDiff;
            var low = last + openDiff + lowDiff;
            last = close;

            EnsureLength(data, offset + 8);
            var volume = (long)DecodeCompressedNumber(BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4)));
            offset += 4;
            if (type is TdxKlineType.Minute or TdxKlineType.Minute5 or TdxKlineType.Minute15 or TdxKlineType.Minute30 or TdxKlineType.Minute60 or TdxKlineType.Day2)
                volume /= 100;

            var amount = DecodeCompressedNumber(BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4)));
            offset += 4;

            var upCount = 0;
            var downCount = 0;
            if (isIndex && offset + 4 <= data.Length)
            {
                volume *= 100;
                upCount = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));
                downCount = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset + 2, 2));
                offset += 4;
            }

            items.Add(new TdxKlineItem(time, ToPrice(open), ToPrice(high), ToPrice(low), ToPrice(close), volume, amount, upCount, downCount));
        }

        return new TdxKlineResponse(count, items);
    }

    static TdxTradeResponse DecodeTrades(byte[] data, DateTime date, string fullCode, bool hasOrderCount, bool skipUnknownHeader)
    {
        EnsureLength(data, 2);
        var count = BinaryPrimitives.ReadUInt16LittleEndian(data);
        var offset = skipUnknownHeader ? 6 : 2;
        var items = new List<TdxTradeItem>(count);
        var lastPriceRaw = 0L;
        var basePrice = TdxCode.IsEtf(fullCode) ? 10L : 1L;

        for (var i = 0; i < count && offset + 2 <= data.Length; i++)
        {
            var minuteOfDay = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));
            offset += 2;
            var time = date.Date.AddMinutes(minuteOfDay);

            var priceDiff = ReadVarInt(data, ref offset);
            lastPriceRaw += priceDiff * 10L;
            var price = ToPrice(lastPriceRaw / basePrice);
            var volume = ReadVarInt(data, ref offset);
            var number = hasOrderCount ? ReadVarInt(data, ref offset) : 0;
            var status = ReadVarInt(data, ref offset);
            _ = ReadVarInt(data, ref offset);

            items.Add(new TdxTradeItem(time, price, volume, number, status));
        }

        return new TdxTradeResponse(count, items);
    }

    static TdxCallAuctionResponse DecodeCallAuction(byte[] data)
    {
        EnsureLength(data, 2);
        var count = BinaryPrimitives.ReadUInt16LittleEndian(data);
        var offset = 2;
        var now = DateTime.Now;
        var items = new List<TdxCallAuctionItem>(count);

        for (var i = 0; i < count && offset + 16 <= data.Length; i++, offset += 16)
        {
            var minuteOfDay = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));
            var hour = minuteOfDay / 60;
            var minute = minuteOfDay % 60;
            var price = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset + 2, 4));
            var matchVolume = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset + 6, 2));
            var unmatched = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(offset + 10, 2));
            var second = data[offset + 15];
            var flag = unmatched < 0 ? -1 : 1;
            items.Add(new TdxCallAuctionItem(
                new DateTime(now.Year, now.Month, now.Day, hour, minute, second),
                price,
                matchVolume,
                Math.Abs(unmatched),
                flag));
        }

        return new TdxCallAuctionResponse(count, items);
    }

    static TdxKRaw DecodeK(byte[] data, ref int offset)
    {
        var close = ReadVarInt(data, ref offset);
        var last = close + ReadVarInt(data, ref offset);
        var open = close + ReadVarInt(data, ref offset);
        var high = close + ReadVarInt(data, ref offset);
        var low = close + ReadVarInt(data, ref offset);
        return new TdxKRaw(last * 10L, open * 10L, high * 10L, low * 10L, close * 10L);
    }

    static DateTime DecodeKlineTime(ReadOnlySpan<byte> data, TdxKlineType type)
    {
        if (type is TdxKlineType.Minute or TdxKlineType.Minute2 or TdxKlineType.Minute5 or TdxKlineType.Minute15 or TdxKlineType.Minute30 or TdxKlineType.Minute60)
        {
            var yearMonthDay = BinaryPrimitives.ReadUInt16LittleEndian(data[..2]);
            var hourMinute = BinaryPrimitives.ReadUInt16LittleEndian(data[2..4]);
            var year = (yearMonthDay >> 11) + 2004;
            var month = (yearMonthDay % 2048) / 100;
            var day = (yearMonthDay % 2048) % 100;
            var hour = hourMinute / 60;
            var minute = hourMinute % 60;
            return new DateTime(year, month, day, hour, minute, 0);
        }

        var ymd = BinaryPrimitives.ReadUInt32LittleEndian(data);
        var y = (int)(ymd / 10000);
        var m = (int)((ymd % 10000) / 100);
        var d = (int)(ymd % 100);
        return new DateTime(y, m, d, 15, 0, 0);
    }

    static int ReadVarInt(byte[] data, ref int offset)
    {
        var value = 0;
        var i = 0;
        byte first = 0;

        while (offset < data.Length)
        {
            var b = data[offset++];
            if (i == 0)
            {
                first = b;
                value += b & 0x3F;
            }
            else
            {
                value += (b & 0x7F) << (6 + (i - 1) * 7);
            }

            i++;
            if ((b & 0x80) == 0) break;
        }

        return (first & 0x40) > 0 ? -value : value;
    }

    static double DecodeCompressedNumber(uint value)
    {
        var ivol = unchecked((int)value);
        var logpoint = ivol >> 24;
        var hleax = (ivol >> 16) & 0xff;
        var lheax = (ivol >> 8) & 0xff;
        var lleax = ivol & 0xff;

        var dwEcx = logpoint * 2 - 0x7f;
        var dwEdx = logpoint * 2 - 0x86;
        var dwEsi = logpoint * 2 - 0x8e;
        var dwEax = logpoint * 2 - 0x96;

        var abs = Math.Abs(dwEcx);
        var baseValue = Math.Pow(2.0, abs);
        if (dwEcx < 0) baseValue = 1.0 / baseValue;

        double part2;
        if (hleax > 0x80)
        {
            part2 = Math.Pow(2.0, dwEdx) * 128.0;
            part2 += (hleax & 0x7f) * Math.Pow(2.0, dwEdx + 1);
        }
        else
        {
            part2 = dwEdx >= 0 ? Math.Pow(2.0, dwEdx) * hleax : (1 / Math.Pow(2.0, dwEdx)) * hleax;
        }

        var part3 = Math.Pow(2.0, dwEsi) * lheax;
        var part4 = Math.Pow(2.0, dwEax) * lleax;
        if ((hleax & 0x80) > 0)
        {
            part3 *= 2.0;
            part4 *= 2.0;
        }

        return baseValue + part2 + part3 + part4;
    }

    static double ToPrice(long raw) => raw / 1000.0;

    static uint ParseDateNumber(string date)
    {
        if (!DateTime.TryParseExact(date, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            throw new ArgumentException("日期格式应为 yyyyMMdd。", nameof(date));
        return uint.Parse(date, CultureInfo.InvariantCulture);
    }

    static string DecodeGbk(ReadOnlySpan<byte> bytes)
    {
        var trimmed = bytes.ToArray().TakeWhile(b => b != 0).ToArray();
        return GbkEncoding.GetString(trimmed).Trim();
    }

    static Encoding CreateGbkEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding("GBK");
    }

    static byte[] ZlibDecompress(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return output.ToArray();
    }

    static async Task ReadExactAsync(NetworkStream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken);
            if (read <= 0) throw new IOException("连接断开。");
            offset += read;
        }
    }

    static void WriteUInt16LE(Stream stream, ushort value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    static void WriteUInt32LE(Stream stream, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    static void EnsureLength(byte[] data, int length)
    {
        if (data.Length < length)
            throw new InvalidDataException($"响应数据长度不足，预期至少 {length} 字节，实际 {data.Length} 字节。");
    }

    NetworkStream RequireStream() => _stream ?? throw new InvalidOperationException("尚未连接，请先调用 ConnectAsync。");

    public ValueTask DisposeAsync()
    {
        _stream?.Dispose();
        _tcp.Dispose();
        return ValueTask.CompletedTask;
    }
}

public enum TdxExchange : byte
{
    Sz = 0,
    Sh = 1,
    Bj = 2
}

public enum TdxKlineType : byte
{
    Minute5 = 0,
    Minute15 = 1,
    Minute30 = 2,
    Minute60 = 3,
    Day2 = 4,
    Week = 5,
    Month = 6,
    Minute = 7,
    Minute2 = 8,
    Day = 9,
    Quarter = 10,
    Year = 11
}

public static class TdxCode
{
    public static TdxExchange ExchangeFromText(string text) => text.ToLowerInvariant() switch
    {
        "sz" or "0" or "深" or "深圳" => TdxExchange.Sz,
        "sh" or "1" or "沪" or "上海" => TdxExchange.Sh,
        "bj" or "2" or "北" or "北京" => TdxExchange.Bj,
        _ => throw new ArgumentException($"未知市场: {text}")
    };

    public static (TdxExchange Exchange, string Number) Decode(string code)
    {
        code = AddPrefix(code);
        if (code.Length != 8)
            throw new ArgumentException("证券代码长度错误，例如 sz000001。", nameof(code));

        return code[..2].ToLowerInvariant() switch
        {
            "sz" => (TdxExchange.Sz, code[2..]),
            "sh" => (TdxExchange.Sh, code[2..]),
            "bj" => (TdxExchange.Bj, code[2..]),
            _ => throw new ArgumentException("证券代码市场前缀错误。", nameof(code))
        };
    }

    public static string AddPrefix(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("代码不能为空");

        code = code.Trim().ToLowerInvariant();
        if (code.StartsWith("sh") || code.StartsWith("sz") || code.StartsWith("bj"))
            return code;

        if (IsShStock(code) || IsShEtf(code)) return "sh" + code;
        if (IsSzStock(code) || IsSzEtf(code)) return "sz" + code;
        if (IsBjStock(code)) return "bj" + code;

        if (IsShIndex(code)) return "sh" + code;
        if (IsSzIndex(code)) return "sz" + code;
        if (IsBjIndex(code)) return "bj" + code;

        throw new ArgumentException($"无法识别的代码: {code}");
    }

    public static bool IsEtf(string code)
    {
        code = AddPrefix(code);
        var number = code[2..];
        return (code.StartsWith("sh") && IsShEtf(number)) || (code.StartsWith("sz") && IsSzEtf(number));
    }

    public static bool IsIndex(string code)
    {
        code = AddPrefix(code);
        var number = code[2..];
        return (code.StartsWith("sh") && IsShIndex(number))
            || (code.StartsWith("sz") && IsSzIndex(number))
            || (code.StartsWith("bj") && IsBjIndex(number));
    }

    static bool IsShStock(string code) => code.Length == 6 && code.StartsWith('6');
    static bool IsSzStock(string code) => code.Length == 6 && (code.StartsWith('0') || code.StartsWith("30"));
    static bool IsBjStock(string code) => code.Length == 6 && code.StartsWith("92");
    static bool IsShEtf(string code) => code.Length == 6 && (code.StartsWith("50") || code.StartsWith("51") || code.StartsWith("52") || code.StartsWith("53") || code.StartsWith("56") || code.StartsWith("58"));
    static bool IsSzEtf(string code) => code.Length == 6 && (code.StartsWith("15") || code.StartsWith("16"));
    static bool IsShIndex(string code) => code.Length == 6 && (code.StartsWith("000") || code == "999999");
    static bool IsSzIndex(string code) => code.Length == 6 && code.StartsWith("399");
    static bool IsBjIndex(string code) => code.Length == 6 && code.StartsWith("899");
}

public static class TdxExchangeExtensions
{
    public static byte ToByte(this TdxExchange exchange) => (byte)exchange;
    public static string ToPrefix(this TdxExchange exchange) => exchange switch
    {
        TdxExchange.Sz => "sz",
        TdxExchange.Sh => "sh",
        TdxExchange.Bj => "bj",
        _ => "unknown"
    };
}

public sealed record TdxResponse(byte Control, uint MsgId, byte Unknown, ushort Type, byte[] Data);
public sealed record TdxCodeResponse(int Count, IReadOnlyList<TdxCodeInfo> Items);
public sealed record TdxCodeInfo(string Code, string Name, ushort Multiple, sbyte Decimal, double LastPrice);
public sealed record TdxPriceLevel(double Price, int Volume);
public sealed record TdxQuote(TdxExchange Exchange, string Code, string ServerTime, double PreviousClose, double Open, double High, double Low, double Last, int TotalHand, int CurrentVolume, double Amount, int InsideDish, int OuterDisc, IReadOnlyList<TdxPriceLevel> BuyLevels, IReadOnlyList<TdxPriceLevel> SellLevels);
public sealed record TdxMinuteResponse(int Count, IReadOnlyList<TdxMinuteItem> Items);
public sealed record TdxMinuteItem(string Time, double Price, int Volume);
public sealed record TdxKlineResponse(int Count, IReadOnlyList<TdxKlineItem> Items);
public sealed record TdxKlineItem(DateTime Time, double Open, double High, double Low, double Close, long Volume, double Amount, int UpCount, int DownCount);
internal readonly record struct TdxKRaw(long LastRaw, long OpenRaw, long HighRaw, long LowRaw, long CloseRaw);
public sealed record TdxTradeResponse(int Count, IReadOnlyList<TdxTradeItem> Items);
public sealed record TdxTradeItem(DateTime Time, double Price, int Volume, int Number, int Status);
public sealed record TdxCallAuctionResponse(int Count, IReadOnlyList<TdxCallAuctionItem> Items);
public sealed record TdxCallAuctionItem(DateTime Time, double Price, long MatchVolume, long UnmatchedVolume, int Flag);
