namespace VoiceChat.Core.Models;

/// <summary>
/// 语音数据包
/// </summary>
public class VoicePacket
{
    public byte PacketType { get; set; } = 0x01;
    public string UserId { get; set; } = string.Empty;
    public long Timestamp { get; set; }
    public uint SequenceNumber { get; set; }
    public byte[] AudioData { get; set; } = Array.Empty<byte>();
    public int AudioDataLength { get; set; }

/// <summary>
    /// 序列化（每次调用分配新缓冲区，线程安全）
    /// </summary>
    public byte[] Serialize()
    {
        var userIdBytes = System.Text.Encoding.UTF8.GetBytes(UserId ?? "");
        if (userIdBytes.Length > 65535)
        {
            // 超长用户名截断到 32 字符（UTF-8 最多 128 字节，仍在 2 字节长度范围内）
            userIdBytes = System.Text.Encoding.UTF8.GetBytes(UserId!.Substring(0, 32));
        }

        int estimatedSize = 21 + userIdBytes.Length + AudioDataLength;
        var buffer = new byte[estimatedSize];

        int offset = 0;

        // PacketType (1 byte)
        buffer[offset++] = PacketType;

        // UserId (2-byte length-prefixed string, Little Endian)
        buffer[offset++] = (byte)(userIdBytes.Length & 0xFF);
        buffer[offset++] = (byte)((userIdBytes.Length >> 8) & 0xFF);
        System.Array.Copy(userIdBytes, 0, buffer, offset, userIdBytes.Length);
        offset += userIdBytes.Length;

        // Timestamp (8 bytes)
        System.Array.Copy(BitConverter.GetBytes(Timestamp), 0, buffer, offset, 8);
        offset += 8;

        // SequenceNumber (4 bytes)
        System.Array.Copy(BitConverter.GetBytes(SequenceNumber), 0, buffer, offset, 4);
        offset += 4;

        // AudioDataLength (4 bytes)
        System.Array.Copy(BitConverter.GetBytes(AudioDataLength), 0, buffer, offset, 4);
        offset += 4;

        // AudioData
        if (AudioDataLength > 0 && AudioData != null)
        {
            System.Array.Copy(AudioData, 0, buffer, offset, AudioDataLength);
            offset += AudioDataLength;
        }

        // 返回实际大小的数组
        if (offset < buffer.Length)
        {
            var result = new byte[offset];
            System.Array.Copy(buffer, result, offset);
            return result;
        }
        return buffer;
    }
/// <summary>
    /// 从字节数组反序列化
    /// </summary>
    public static VoicePacket? Deserialize(byte[] data)
    {
        var packets = DeserializeMultiple(data);
        return packets.Count > 0 ? packets[0] : null;
    }

    /// <summary>
    /// 从字节数组反序列化多个包（支持合并包）
    /// </summary>
    public static List<VoicePacket> DeserializeMultiple(byte[] data)
    {
        // 限制输入数据大小，防止恶意数据导致 OOM（正常最大 ~500 bytes/包，2包=1000 bytes）
        if (data == null || data.Length > 65535) return new List<VoicePacket>();

        var packets = new List<VoicePacket>();
        int offset = 0;

        // 限制最大解析包数，防止恶意数据导致 OOM（正常 20ms 帧最多 2 包）
        const int MaxPacketsPerDatagram = 10;

        while (offset < data.Length && packets.Count < MaxPacketsPerDatagram)
        {
            try
            {
                // 检查是否有足够的数据读取最小包头 (1 + 2 + 8 + 4 + 4 = 19 bytes)
                if (offset + 19 > data.Length) break;

                var packet = new VoicePacket();

                // PacketType (1 byte)
                packet.PacketType = data[offset++];

                // UserId (2-byte length-prefixed string, Little Endian)
                if (offset + 2 > data.Length) break;
                int userIdLen = data[offset] | (data[offset + 1] << 8);
                offset += 2;
                if (userIdLen <= 0 || userIdLen > 65535 || offset + userIdLen > data.Length)
                {
                    break; // 无效的UserId长度，数据可能损坏
                }
                packet.UserId = System.Text.Encoding.UTF8.GetString(data, offset, userIdLen);
                offset += userIdLen;

                // Timestamp (8 bytes)
                if (offset + 8 > data.Length) break;
                packet.Timestamp = BitConverter.ToInt64(data, offset);
                offset += 8;

                // SequenceNumber (4 bytes)
                if (offset + 4 > data.Length) break;
                packet.SequenceNumber = BitConverter.ToUInt32(data, offset);
                offset += 4;

                // AudioDataLength (4 bytes)
                if (offset + 4 > data.Length) break;
                packet.AudioDataLength = BitConverter.ToInt32(data, offset);
                offset += 4;

                // AudioData
                if (packet.AudioDataLength < 0 || packet.AudioDataLength > 100000)
                {
                    break; // 无效长度
                }

                if (packet.AudioDataLength > 0)
                {
                    if (offset + packet.AudioDataLength > data.Length) break;
                    packet.AudioData = new byte[packet.AudioDataLength];
                    System.Array.Copy(data, offset, packet.AudioData, 0, packet.AudioDataLength);
                    offset += packet.AudioDataLength;
                }

                packets.Add(packet);
            }
            catch
            {
                break; // 解析失败，退出循环
            }
        }

        return packets;
    }
}
