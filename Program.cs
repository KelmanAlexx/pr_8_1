using System.Text;
using AutoUpdater.Models;
using AutoUpdater.Services;

namespace AutoUpdater;

internal class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.Title = "Auto-Updater";

        string configPath = Path.Combine(AppContext.BaseDirectory, "updater.json");
        AppConfig config;

        try
        {
            config = AppConfig.Load(configPath);
        }
        catch (Exception exception)
        {
            Console.WriteLine("Не удалось загрузить файл updater.json.");
            Console.WriteLine("Подробности: " + exception.Message);
            return 1;
        }

        using HttpClient httpClient = new();
        httpClient.Timeout = TimeSpan.FromMinutes(5);

        UpdateService updateService = new(
            httpClient,
            config,
            configPath,
            AppContext.BaseDirectory);

        try
        {
            if (args.Length > 0)
            {
                string command = args[0].ToLowerInvariant();

                if (command == "check")
                {
                    await updateService.CheckForUpdatesAsync();
                    return 0;
                }

                if (command == "update")
                {
                    await updateService.UpdateAsync();
                    return 0;
                }

                if (command == "help" || command == "--help" || command == "-h")
                {
                    ShowHelp();
                    return 0;
                }

                Console.WriteLine("Неизвестная команда: " + args[0]);
                ShowHelp();
                return 1;
            }

            await ShowMenuAsync(updateService);
            return 0;
        }
        catch (Exception exception)
        {
            Console.WriteLine();
            Console.WriteLine("Во время работы произошла ошибка:");
            Console.WriteLine(exception.Message);
            return 1;
        }
    }

    private static async Task ShowMenuAsync(UpdateService updateService)
    {
        bool isRunning = true;

        while (isRunning)
        {
            Console.WriteLine();
            Console.WriteLine("====================================");
            Console.WriteLine("          AUTO-UPDATER");
            Console.WriteLine("====================================");
            Console.WriteLine("1. Проверить наличие обновлений");
            Console.WriteLine("2. Проверить и установить обновление");
            Console.WriteLine("0. Выход");
            Console.Write("Выберите действие: ");

            string? choice = Console.ReadLine();

            try
            {
                switch (choice)
                {
                    case "1":
                        await updateService.CheckForUpdatesAsync();
                        break;
                    case "2":
                        await updateService.UpdateAsync();
                        break;
                    case "0":
                        isRunning = false;
                        break;
                    default:
                        Console.WriteLine("Такого пункта нет. Попробуйте ещё раз.");
                        break;
                }
            }
            catch (Exception exception)
            {
                Console.WriteLine("Ошибка: " + exception.Message);
            }
        }

        Console.WriteLine("Работа программы завершена.");
    }

    private static void ShowHelp()
    {
        Console.WriteLine();
        Console.WriteLine("Auto-Updater — утилита проверки и установки обновлений.");
        Console.WriteLine();
        Console.WriteLine("Команды:");
        Console.WriteLine("  AutoUpdater check   — проверить наличие обновления");
        Console.WriteLine("  AutoUpdater update  — проверить и установить обновление");
        Console.WriteLine("  AutoUpdater help    — показать эту справку");
        Console.WriteLine();
        Console.WriteLine("Настройки находятся в файле updater.json рядом с программой.");
    }
}
