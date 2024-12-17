using System;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Collections.Generic;
using System.Net.Http;
using Newtonsoft.Json;
using System.Linq;
using LeiShen;

public class LeiShengService
{
    //错误码 -1 token失效
    //错误码 -2 未知错误
    //其他错误码参考如下
    public const int HTTP_SUCCESS_NET_CODE = 0;
    public const int HTTP_TOKEN_EXPIRE = 400006;
    public const int HTTP_LOGIN_ERROR_CODE = 400003;
    public const int HTTP_ERROR_NEW_CODE = 0x5e4;
    public const int HTTP_ERROR_NOT_PAY = 400877;
    public const int HTTP_ERROR_WX_NOBIND = 400617;
    public const int HTTP_ERROR_JYCODE = 400857;
    public const int EXPERIENCE_END_TIME = 400855;



    private const string loginURL = "https://webapi.leigod.com/api/auth/login/v1";
    private const string pauseURL = "https://webapi.leigod.com/api/user/pause";
    private const string InfoURL = "https://webapi.leigod.com/api/user/info";
    private const string KEY = "5C5A639C20665313622F51E93E3F2783";

    private readonly HttpClient httpClient;
    private readonly Dictionary<string, string> header;
    private string accountToken = "";
    private DateTime experienceEndTime = DateTime.MinValue;
    public LeiShengService(string accountToken = null)
    {
        this.accountToken = accountToken;
        httpClient = new HttpClient();
        header = new Dictionary<string, string>
        {
           {"User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/88.0.4324.96 Safari/537.36 Edg/88.0.705.53"},
           {"Content-Type",  "application/x-www-form-urlencoded; charset=UTF-8"},
           {"Connection",  "keep-alive"},
           {"Accept",  "application/json, text/javascript, */*; q=0.01"},
           {"Accept-Encoding",  "gzip, deflate, br"},
           {"Accept-Language",  "zh-CN,zh;q=0.9,en;q=0.8,en-GB;q=0.7,en-US;q=0.6"},
           {"DNT",  "1"},
           {"Referer",  "https://www.legod.com/"},
           {"Sec-Fetch-Dest",  "empty"},
           {"Sec-Fetch-Mode",  "cors"},
           {"Sec-Fetch-Site",  "same-site" },
        };
    }

    public DateTime GetExperienceEndTime()
    {
        return experienceEndTime;
    }
    public string GetAccountToken()
    {
        return accountToken;
    }

    public async Task<KeyValuePair<bool, string>> LoginAsync(string uname, string password)
    {
        // 输入校验
        if (string.IsNullOrEmpty(uname) || string.IsNullOrEmpty(password))
        {
            return new KeyValuePair<bool, string>(false, "用户名或密码不能为空");
        }

        // 构造请求体
        var body = new Dictionary<string, string>
        {
            { "account_token", "null" },
            { "country_code", "86" },
            { "lang", "zh_CN" },
            { "mobile_num", uname },
            { "os_type", "4" },
            { "password", GenerateMD5(password) },
            { "region_code", "1" },
            { "src_channel", "guanwang" },
            { "username", uname }
        };

        // 对请求体做签名
        LegodSign(body);

        // 将请求体转换为 URL 编码格式
        var content = new FormUrlEncodedContent(body);

        try
        {
            // 发送 POST 请求
            var response = await httpClient.PostAsync(loginURL, content);
            response.EnsureSuccessStatusCode(); // 确保返回状态为 2xx

            // 解析响应内容
            var jsonResponse = await response.Content.ReadAsStringAsync();
            var msg = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonResponse);

            if (msg.TryGetValue("code", out var code) && Convert.ToInt32(code) == 0)
            {
                dynamic data = msg["data"];
                var token = data.login_info.account_token.ToString();
                accountToken = token;
                return new KeyValuePair<bool, string>(true, token);
            }
            else
            {
                return new KeyValuePair<bool, string>(false, "errorCode: {code}  " + (msg.TryGetValue("msg", out object value) ? value.ToString() : "未知错误"));
            }
        }
        catch (HttpRequestException e)
        {
            return new KeyValuePair<bool, string>(false, $"请求失败: {e.Message}");
        }
        catch (Exception e)
        {
            return new KeyValuePair<bool, string>(false, $"发生错误: {e.Message}");
        }
    }


    public async Task<int> PauseAsync()
    {
        // 请求暂停
        var payload = new Dictionary<string, string>
            {
                { "account_token",  this.accountToken },
                { "lang", "zh_CN" },
                { "os_type", "4" }
            };

        try
        {
            var content = new FormUrlEncodedContent(payload);
            var response = await httpClient.PostAsync(pauseURL, content);
            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                return -1;
            }

            // 解析响应内容
            var jsonResponse = await response.Content.ReadAsStringAsync();
            dynamic msg = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonResponse);
            Program.ConsoleWriteLine("暂停结果：" + msg["msg"].ToString());
            if (!msg.TryGetValue("code", out object code))
            {
                return -2;
            }
            var intcode = Convert.ToInt32(code);
            return intcode;
        }
        catch (HttpRequestException ex)
        {
            // 未知错误
            return 404;
        }
        catch (Exception ex)
        {
            Program.ConsoleWriteLine("未知错误，可能是请求频繁或者是网址更新: " + ex.Message);
            return -2;
        }
    }

    public async Task<int> GetIsPause()
    {
        // 构建请求体
        var payload = new Dictionary<string, string>
        {
            { "account_token", this.accountToken },
            { "lang", "zh_CN" },
            { "os_type", "4" }
        };

        try
        {
            // 发送 POST 请求
            var content = new FormUrlEncodedContent(payload);
            var response = await httpClient.PostAsync(InfoURL, content);
            response.EnsureSuccessStatusCode();  // 确保 HTTP 请求成功

            // 解析响应内容
            var jsonResponse = await response.Content.ReadAsStringAsync();
            var msg = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonResponse);
            msg.TryGetValue("code", out var code);
            if (Convert.ToInt32(code) == HTTP_TOKEN_EXPIRE)
            {
                return -1; //登陆失败
            }
            else if (Convert.ToInt32(code) == 0)
            {
                dynamic msgdata = msg["data"];
                try
                {
                    var experienceTime = msgdata.expired_experience_time.Value.ToString();
                    this.experienceEndTime = DateTime.Parse(experienceTime);
                    if (experienceEndTime > DateTime.Now)
                    {
                        return EXPERIENCE_END_TIME;
                    }
                }
                catch { }

                var ispause = msgdata.pause_status_id.Value;
                // 返回状态
                return int.Parse(ispause.ToString());
            }
            else
            {
                // 未知错误
                return Convert.ToInt32(code);
            }
        }
        catch (HttpRequestException ex)
        {
            // 未知错误
            return 404;
        }
        catch (Exception ex)
        {
            // 未知错误
            return -2;
        }
    }


    // MD5 加密
    private string GenerateMD5(string input)
    {
        using (var md5 = MD5.Create())
        {
            var inputBytes = Encoding.UTF8.GetBytes(input);
            var hashBytes = md5.ComputeHash(inputBytes);
            var sb = new StringBuilder();
            foreach (var b in hashBytes)
            {
                sb.Append(b.ToString("x2")); // 转换为16进制字符串
            }
            return sb.ToString();
        }
    }

    // 签名请求体的示例方法
    private void LegodSign(Dictionary<string, string> body)
    {


        // body["ts"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        body["ts"] = ((long)(DateTime.Now - new DateTime(1970, 1, 1, 8, 0, 0)).TotalSeconds).ToString();


        // 添加时间戳


        // 生成待签名字符串
        var sortedParams = body
            .OrderBy(kvp => kvp.Key) // 按键顺序排序
            .Select(kvp => $"{kvp.Key}={kvp.Value}") // 将键值对转换为字符串格式
            .ToArray();

        // 将所有参数连接为一个字符串，并添加密钥
        string strToSign = string.Join("&", sortedParams) + "&key=" + KEY;

        // 打印待签名字符串（调试时使用）
        Program.ConsoleWriteLine("sign: " + strToSign);

        // 生成签名并添加到请求体
        body["sign"] = GenerateMD5(strToSign);
    }
}