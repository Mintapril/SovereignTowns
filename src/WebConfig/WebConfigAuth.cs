using System;
using System.IO;
using System.Security.Cryptography;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.WebConfig;

/// <summary>
/// 启动时生成随机 token 写到 <c>Documents/.../SovereignTowns/auth.txt</c>。
/// 任何 API 请求必须携带 header <c>X-ST-Token</c> 与该 token 匹配，否则 401。
///
/// <para>
/// 浏览器端在玩家从 MCM 入口启动时通过 URL query param 拿到 token（<c>?t=xxx</c>），
/// 存入 sessionStorage 后续 fetch 都附带 header。其他本机进程无法读 auth.txt（Windows ACL
/// 默认仅当前用户可读 %USERPROFILE% 下文件），就读不到 token，也就调不了 API。
/// </para>
/// </summary>
public static class WebConfigAuth
{
    private const string AuthFileName = "auth.txt";
    private const int TokenByteLength = 32;

    private static string _token = "";

    /// <summary>启动 server 时调用。生成 token 并写盘。返回 token 字符串供后续 URL 构造。</summary>
    public static string GenerateAndPersist()
    {
        try
        {
            byte[] buf = new byte[TokenByteLength];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(buf);
            }
            // URL-safe base64：替换 + / =
            _token = Convert.ToBase64String(buf)
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');

            string path = GetAuthFilePath();
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(path, _token);
            Logger.Info($"WebConfigAuth: token written to '{path}'");
            return _token;
        }
        catch (Exception ex)
        {
            Logger.Error("WebConfigAuth.GenerateAndPersist failed", ex);
            _token = "";
            return "";
        }
    }

    /// <summary>当前 token。Generate 之前调用返回空串。</summary>
    public static string CurrentToken => _token;

    /// <summary>
    /// 校验请求头 / query 里的 token。空 token 或不匹配都返回 false。
    /// 用 ordinal compare 避免 timing-leak 不重要（本地 server），但仍走 fixed-time compare。
    /// </summary>
    public static bool IsAuthorized(string? candidate)
    {
        if (string.IsNullOrEmpty(_token)) return false;
        if (string.IsNullOrEmpty(candidate)) return false;
        if (candidate!.Length != _token.Length) return false;

        int diff = 0;
        for (int i = 0; i < _token.Length; i++)
        {
            diff |= _token[i] ^ candidate[i];
        }
        return diff == 0;
    }

    private static string GetAuthFilePath()
    {
        return Path.Combine(TroopDumper.GetBaseDirectory(), AuthFileName);
    }
}
