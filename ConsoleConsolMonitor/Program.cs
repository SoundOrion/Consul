using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Consul;

class Program
{
    static async Task Main()
    {
        string serviceId = "process-orders";

        //using var client = new ConsulClient();
        using var client = new ConsulClient(config => config.Address = new Uri("http://localhost:8500"));

        Console.CancelKeyPress += async (sender, eventArgs) =>
        {
            Console.WriteLine("サービスを停止中...");
            await client.Agent.ServiceDeregister(serviceId);
            Console.WriteLine("サービスを正常に登録解除しました");
            eventArgs.Cancel = true; // 強制終了を防ぐ
        };

        // Consul に自身のサービス登録
        await client.Agent.ServiceRegister(new AgentServiceRegistration
        {
            ID = serviceId,
            Name = serviceId,
            Address = "127.0.0.1",
            Port = 0, // EXE にはポートがないので 0
            Check = new AgentServiceCheck
            {
                TTL = TimeSpan.FromSeconds(15), // 15秒ごとのヘルスチェック
                Notes = "Self-healthcheck process",
                Name = "Self-Check"
            }
        });

        Console.WriteLine("Consul にサービスを登録しました。");

        // メインの監視ループ
        while (true)
        {
            if (IsProcessRunning())
            {
                await client.Agent.PassTTL($"service:{serviceId}", "OK");
            }
            else
            {
                Console.WriteLine("プロセスが動作していません。Consul にエラーを通知します...");
                await client.Agent.FailTTL($"service:{serviceId}", "Process not running!");
                break; // ループを抜ける
            }

            await Task.Delay(10000); // 10秒ごとにチェック
        }

        // Consul から登録解除
        await client.Agent.ServiceDeregister(serviceId);
        Console.WriteLine("サービスが異常終了したため、Consul から登録解除しました。");
    }

    /// <summary>
    /// EXE（自身）が正常に動作しているか確認
    /// </summary>
    static bool IsProcessRunning()
    {
        try
        {
            using Process currentProcess = Process.GetCurrentProcess();
            return !currentProcess.HasExited;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"プロセスチェック中にエラー: {ex.Message}");
            return false;
        }
    }
}


public static class NetworkHelper
{
    /// <summary>
    /// 動作中のネットワークインターフェイスから IPv4 のローカル IP アドレスを取得します。
    /// （ループバックアドレスは除外）
    /// </summary>
    /// <returns>見つかった場合は IPv4 アドレス（文字列）、見つからなければ null</returns>
    public static string GetLocalIPv4Address()
    {
        // 全ネットワークインターフェイスのうち、動作中かつループバックでないものを対象とする
        var activeInterfaces = NetworkInterface.GetAllNetworkInterfaces()
            .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                         ni.NetworkInterfaceType != NetworkInterfaceType.Loopback);

        foreach (var ni in activeInterfaces)
        {
            var ipProps = ni.GetIPProperties();

            // Unicast アドレスのうち、IPv4 でループバックでないものを選択
            var ipv4Address = ipProps.UnicastAddresses
                .Where(ip => ip.Address.AddressFamily == AddressFamily.InterNetwork &&
                             !IPAddress.IsLoopback(ip.Address))
                .Select(ip => ip.Address.ToString())
                .FirstOrDefault();

            if (!string.IsNullOrEmpty(ipv4Address))
            {
                return ipv4Address;
            }
        }

        return null; // IPv4 アドレスが見つからなかった場合
    }
}
