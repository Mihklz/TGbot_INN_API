using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;

namespace TelegramBotExperiments
{
    class Program
    {
        // Инициализация клиента Telegram бота
        private static ITelegramBotClient bot;

        // Переменные для хранения последнего действия и команды
        private static string lastAction;
        private static string lastCommand;

        // Переменные для хранения API ключей DaData
        private static string daDataApiKey;
        private static string daDataSecretKey;

        static async Task Main(string[] args)
        {
            // Чтение конфигурационного файла appsettings.json
            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json")
                .Build();

            // Получение токена бота и API ключей DaData из конфигурации
            var botToken = config["BotConfiguration:BotToken"];
            daDataApiKey = config["BotConfiguration:DaDataApiKey"];
            daDataSecretKey = config["BotConfiguration:DaDataSecretKey"];

            // Инициализация клиента бота с полученным токеном
            bot = new TelegramBotClient(botToken);

            Console.WriteLine("Запущен бот " + bot.GetMeAsync().Result.FirstName);

            // Настройка отмены токенов и параметров приёма сообщений
            var cts = new CancellationTokenSource();
            var cancellationToken = cts.Token;
            var receiverOptions = new ReceiverOptions
            {
                AllowedUpdates = { }, // Приём всех типов обновлений
            };

            // Запуск приёма сообщений
            bot.StartReceiving(
                HandleUpdateAsync,
                HandleErrorAsync,
                receiverOptions,
                cancellationToken
            );
            Console.ReadLine();
        }

        // Обработка входящих обновлений
        public static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            if (update.Type == UpdateType.Message && update.Message.Text != null)
            {
                var message = update.Message;
                try
                {
                    // Обработка разных команд от пользователя
                    var command = message.Text.Split(' ')[0].ToLower();
                    switch (command)
                    {
                        case "/start":
                            lastAction = "Приветствую!";
                            lastCommand = "/start";
                            await botClient.SendTextMessageAsync(message.Chat, lastAction);
                            break;
                        case "/help":
                            lastAction = "Доступные команды:\n/start\n/help\n/hello\n/inn <ИНН1> <ИНН2> ...\n/last";
                            lastCommand = "/help";
                            await botClient.SendTextMessageAsync(message.Chat, lastAction);
                            break;
                        case "/hello":
                            lastAction = "Имя: Михаил Кляузин\nEmail: mihanich.krut@gmail.com\nGitHub: https://github.com/Mihklz";
                            lastCommand = "/hello";
                            await botClient.SendTextMessageAsync(message.Chat, lastAction);
                            break;
                        case "/inn":
                            lastCommand = "/inn";
                            string[] innNumbers = message.Text.Split(' ')[1..];
                            if (innNumbers.Length == 0 || string.IsNullOrWhiteSpace(innNumbers[0]))
                            {
                                lastAction = "Пожалуйста, укажите один или несколько ИНН после команды /inn.";
                            }
                            else
                            {
                                lastAction = string.Empty;
                                foreach (var inn in innNumbers)
                                {
                                    if (!string.IsNullOrWhiteSpace(inn))
                                    {
                                        lastAction += await GetCompanyInfoByInn(inn.Trim());
                                    }
                                    else
                                    {
                                        lastAction += "ИНН не может быть пустым.\n";
                                    }
                                }
                            }
                            await botClient.SendTextMessageAsync(message.Chat, lastAction);
                            break;
                        case "/last":
                            await botClient.SendTextMessageAsync(message.Chat, lastAction ?? "Нет предыдущего действия.");
                            break;
                        default:
                            lastAction = "Неизвестная команда. Используйте /help для получения списка команд.";
                            lastCommand = message.Text;
                            await botClient.SendTextMessageAsync(message.Chat, lastAction);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    await HandleErrorAsync(botClient, ex, cancellationToken);
                }
            }
            else
            {
                // Если сообщение не текстовое или пустое
                await botClient.SendTextMessageAsync(update.Message.Chat, "Пожалуйста, отправьте текстовое сообщение. Используйте /help для получения списка команд.");
            }
        }

        // Обработка ошибок
        public static async Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            var errorMessage = exception switch
            {
                ApiRequestException apiRequestException => $"Ошибка телеграм API:\n{apiRequestException.ErrorCode}\n{apiRequestException.Message}",
                _ => exception.ToString()
            };

            Console.WriteLine(errorMessage);
        }

        // Получение информации о компании по ИНН с использованием API DaData
        private static async Task<string> GetCompanyInfoByInn(string inn)
        {
            using var httpClient = new HttpClient();
            try
            {
                // Формирование URL для запроса
                var requestUri = $"https://suggestions.dadata.ru/suggestions/api/4_1/rs/findById/party";
                var requestBody = new JObject
                {
                    { "query", inn }
                };

                var requestContent = new StringContent(requestBody.ToString());
                requestContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");

                // Создание HttpRequestMessage
                var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
                request.Content = requestContent;
                request.Headers.Authorization = new AuthenticationHeaderValue("Token", daDataApiKey);
                request.Headers.Add("X-Secret", daDataSecretKey);

                var response = await httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    JObject companyData = JObject.Parse(content);

                    // Проверка наличия данных в ответе
                    if (companyData["suggestions"] is JArray suggestions && suggestions.Count > 0)
                    {
                        var company = suggestions[0]["data"];
                        var name = company["name"]["short_with_opf"]?.ToString();
                        var address = company["address"]["value"]?.ToString();

                        return $"Информация по ИНН {inn}: Название: {name}, Адрес: {address}\n";
                    }
                    else
                    {
                        return $"Не удалось получить корректные данные по ИНН {inn}\n";
                    }
                }
                else
                {
                    return $"Не удалось получить данные по ИНН {inn}. Код ошибки: {response.StatusCode}\n";
                }
            }
            catch (HttpRequestException e)
            {
                return $"Ошибка при обращении к API DaData для ИНН {inn}: {e.Message}\n";
            }
        }
    }
}

