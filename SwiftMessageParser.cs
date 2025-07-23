//             var parsedMessage = new SwiftMessageParser().Parse(item.mensaje);

namespace Swift
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Provides functionality to parse SWIFT messages into structured objects.
    /// </summary>
    public class SwiftMessageParser
    {
        private const string BlockPattern = @"\{(\d):(.*?)\}";
        private const string TagPattern = @":(\d{2}[A-Z]?):([^:]+)";

        /// <summary>
        /// Parses a SWIFT message string and returns a structured <see cref="SwiftMessage"/> object.
        /// </summary>
        /// <param name="swiftMessage">The SWIFT message string to parse.</param>
        /// <returns>A <see cref="SwiftMessage"/> object containing the parsed data.</returns>
        public SwiftMessage Parse(string swiftMessage)
        {
            if (string.IsNullOrWhiteSpace(swiftMessage))
            {
                throw new ArgumentException("El mensaje SWIFT no puede estar vacío.");
            }

            var message = new SwiftMessage();
            var blocks = this.ExtractBlocks(swiftMessage);

            foreach (var block in blocks)
            {
                switch (block.Key)
                {
                    case "1":
                        this.ParseBasicHeader(block.Value, message);
                        break;
                    case "2":
                        this.ParseApplicationHeader(block.Value, message);
                        break;
                    case "3":
                        this.ParseUserHeader(block.Value, message);
                        break;
                    case "4":
                        this.ParseTextBlock(block.Value, message);
                        break;
                    case "5":
                        this.ParseTrailer(block.Value, message);
                        break;
                }
            }

            return message;
        }

        private Dictionary<string, string> ExtractBlocks(string swiftMessage)
        {
            var blocks = new Dictionary<string, string>();
            var matches = Regex.Matches(swiftMessage, BlockPattern);

            foreach (Match match in matches)
            {
                if (match.Groups.Count >= 3)
                {
                    string blockId = match.Groups[1].Value;
                    string blockContent = match.Groups[2].Value;
                    blocks[blockId] = blockContent;
                }
            }

            return blocks;
        }

        private void ParseBasicHeader(string content, SwiftMessage message)
        {
            if (content.Length < 3)
            {
                return;
            }

            message.BasicHeader = new BasicHeader
            {
                ApplicationId = content.Substring(0, 1),
                ServiceId = content.Substring(1, 2),
                LogicalTerminalAddress = content.Length > 12 ? content.Substring(3, 12) : string.Empty,
                SessionNumber = content.Length > 17 ? content.Substring(15, 4) : string.Empty,
                SequenceNumber = content.Length > 23 ? content.Substring(19, 6) : string.Empty,
            };
        }

        private void ParseApplicationHeader(string content, SwiftMessage message)
        {
            if (content.Length < 2)
            {
                return;
            }

            message.ApplicationHeader = new ApplicationHeader
            {
                InputOutputIdentifier = content.Substring(0, 1),
                MessageType = content.Substring(1, 3),
                InputTime = content.Length > 9 ? content.Substring(4, 4) : string.Empty,
                InputDate = content.Length > 15 ? content.Substring(8, 6) : string.Empty,
                BankPriority = content.Length > 16 ? content.Substring(14, 1) : string.Empty,
                MessageInputReference = content.Length > 31 ? content.Substring(15, 16) : string.Empty,
            };
        }

        private void ParseUserHeader(string content, SwiftMessage message)
        {
            var tags = this.ExtractTags(content);
            message.UserHeader = new UserHeader();

            foreach (var tag in tags)
            {
                switch (tag.Key)
                {
                    case "108":
                        message.UserHeader.MIR = tag.Value;
                        break;
                    case "111":
                        message.UserHeader.ServiceType = tag.Value;
                        break;
                    case "121":
                        message.UserHeader.UniqueEndToEndTransactionReference = tag.Value;
                        break;
                }
            }
        }

        private void ParseTextBlock(string content, SwiftMessage message)
        {
            var tags = this.ExtractTags(content);
            message.TextBlock = new TextBlock();

            foreach (var tag in tags)
            {
                switch (tag.Key)
                {
                    case "20":
                        message.TextBlock.TransactionReferenceNumber = tag.Value;
                        break;
                    case "23B":
                        message.TextBlock.BankOperationCode = tag.Value;
                        break;
                    case "32A":
                        message.TextBlock.CurrencyAmount32A = CurrencyAmount.Parse(tag.Value);
                        break;
                    case "33B":
                        message.TextBlock.CurrencyAmount33B = CurrencyAmount.Parse(tag.Value);
                        break;
                    case "50K":
                        message.TextBlock.OrderingCustomer = PartyInfo.Parse(tag.Value);
                        break;
                    case "52A":
                        message.TextBlock.OrderingInstitution = tag.Value;
                        break;
                    case "59":
                        message.TextBlock.Beneficiary = PartyInfo.Parse(tag.Value);
                        break;
                    case "70":
                        message.TextBlock.DetailsOfPayment = tag.Value;
                        break;
                    case "71A":
                        message.TextBlock.DetailsOfCharges = tag.Value;
                        break;
                    case "71F":
                        // Puede haber múltiples cargos, los agregamos a una lista
                        if (message.TextBlock.Charges == null)
                        {
                            message.TextBlock.Charges = new List<decimal>();
                        }

                        if (decimal.TryParse(tag.Value.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal charge))
                        {
                            message.TextBlock.Charges.Add(charge);
                        }

                        break;
                    case "72":
                        message.TextBlock.SenderToReceiverInformation = tag.Value;
                        break;
                }
            }
        }

        private void ParseTrailer(string content, SwiftMessage message)
        {
            var tags = this.ExtractTags(content);
            message.Trailer = new Trailer();

            foreach (var tag in tags)
            {
                switch (tag.Key)
                {
                    case "MAC":
                        message.Trailer.MessageAuthenticationCode = tag.Value;
                        break;
                    case "CHK":
                        message.Trailer.Checksum = tag.Value;
                        break;
                }
            }
        }

        private Dictionary<string, string> ExtractTags(string content)
        {
            var tags = new Dictionary<string, string>();
            var matches = Regex.Matches(content, TagPattern);

            foreach (Match match in matches)
            {
                if (match.Groups.Count >= 3)
                {
                    string tagId = match.Groups[1].Value;
                    string tagValue = match.Groups[2].Value.Trim();
                    tags[tagId] = tagValue;
                }
            }

            return tags;
        }
    }

    /// <summary>
    /// Represents a parsed SWIFT message.
    /// </summary>
    public class SwiftMessage
    {
        /// <summary>
        /// Gets or sets the basic header block.
        /// </summary>
        public BasicHeader BasicHeader { get; set; }

        /// <summary>
        /// Gets or sets the application header block.
        /// </summary>
        public ApplicationHeader ApplicationHeader { get; set; }

        /// <summary>
        /// Gets or sets the user header block.
        /// </summary>
        public UserHeader UserHeader { get; set; }

        /// <summary>
        /// Gets or sets the text block.
        /// </summary>
        public TextBlock TextBlock { get; set; }

        /// <summary>
        /// Gets or sets the trailer block.
        /// </summary>
        public Trailer Trailer { get; set; }
    }

    /// <summary>
    /// Represents the basic header block of a SWIFT message.
    /// </summary>
    public class BasicHeader
    {
        /// <summary>
        /// Gets or sets the application ID.
        /// </summary>
        public string ApplicationId { get; set; }

        /// <summary>
        /// Gets or sets the service ID.
        /// </summary>
        public string ServiceId { get; set; }

        /// <summary>
        /// Gets or sets the logical terminal address.
        /// </summary>
        public string LogicalTerminalAddress { get; set; }

        /// <summary>
        /// Gets or sets the session number.
        /// </summary>
        public string SessionNumber { get; set; }

        /// <summary>
        /// Gets or sets the sequence number.
        /// </summary>
        public string SequenceNumber { get; set; }
    }

    /// <summary>
    /// Represents the application header block of a SWIFT message.
    /// </summary>
    public class ApplicationHeader
    {
        /// <summary>
        /// Gets or sets the input/output identifier (I = Input, O = Output).
        /// </summary>
        public string InputOutputIdentifier { get; set; }

        /// <summary>
        /// Gets or sets the message type (e.g., 103).
        /// </summary>
        public string MessageType { get; set; }

        /// <summary>
        /// Gets or sets the input time.
        /// </summary>
        public string InputTime { get; set; }

        /// <summary>
        /// Gets or sets the input date.
        /// </summary>
        public string InputDate { get; set; }

        /// <summary>
        /// Gets or sets the bank priority.
        /// </summary>
        public string BankPriority { get; set; }

        /// <summary>
        /// Gets or sets the message input reference.
        /// </summary>
        public string MessageInputReference { get; set; }
    }

    /// <summary>
    /// Represents the user header block of a SWIFT message.
    /// </summary>
    public class UserHeader
    {
        /// <summary>
        /// Gets or sets the Message Input Reference (tag 108).
        /// </summary>
        public string MIR { get; set; }

        /// <summary>
        /// Gets or sets the service type (tag 111).
        /// </summary>
        public string ServiceType { get; set; }

        /// <summary>
        /// Gets or sets the unique end-to-end transaction reference (tag 121).
        /// </summary>
        public string UniqueEndToEndTransactionReference { get; set; }
    }

    /// <summary>
    /// Represents the text block of a SWIFT message.
    /// </summary>
    public class TextBlock
    {
        /// <summary>
        /// Gets or sets the transaction reference number (:20:).
        /// </summary>
        public string TransactionReferenceNumber { get; set; }

        /// <summary>
        /// Gets or sets the bank operation code (:23B:).
        /// </summary>
        public string BankOperationCode { get; set; }

        /// <summary>
        /// Gets or sets the currency (part of :32A:).
        /// </summary>
        public CurrencyAmount CurrencyAmount32A { get; set; }

        /// <summary>
        /// Gets or sets the currency for tag :33B:.
        /// </summary>
        public CurrencyAmount CurrencyAmount33B { get; set; }

        /// <summary>
        /// Gets or sets the ordering customer (:50K:).
        /// </summary>
        public PartyInfo OrderingCustomer { get; set; }

        /// <summary>
        /// Gets or sets the ordering institution (:52A:).
        /// </summary>
        public string OrderingInstitution { get; set; }

        /// <summary>
        /// Gets or sets the beneficiary (:59:).
        /// </summary>
        public PartyInfo Beneficiary { get; set; }

        /// <summary>
        /// Gets or sets the details of payment (:70:).
        /// </summary>
        public string DetailsOfPayment { get; set; }

        /// <summary>
        /// Gets or sets the details of charges (:71A:).
        /// </summary>
        public string DetailsOfCharges { get; set; }

        /// <summary>
        /// Gets or sets the list of charges (:71F:).
        /// </summary>
        public List<decimal> Charges { get; set; }

        /// <summary>
        /// Gets or sets the sender to receiver information (:72:).
        /// </summary>
        public string SenderToReceiverInformation { get; set; }
    }

    /// <summary>
    /// Represents party information (customer, beneficiary, etc.)
    /// </summary>
    public class PartyInfo
    {
        #region Propiedades

        /// <summary>
        /// Account of the party
        /// </summary>
        public string Account { get; set; }

        /// <summary>
        /// Name of the party
        /// </summary>
        public string Name { get; set; }

        #endregion

        #region Métodos públicos

        /// <summary>
        /// Parses party information from a SWIFT field (50K, 59)
        /// </summary>
        /// <param name="input">String with party information</param>
        /// <returns>Parsed PartyInfo object</returns>
        public static PartyInfo Parse(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                throw new ArgumentException("La entrada no puede estar vacía", nameof(input));
            }

            var result = new PartyInfo();
            var parts = input.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            // El primer segmento puede contener la cuenta (ej: /000002426114498)
            if (input.StartsWith("/"))
            {
                var accountEnd = input.IndexOf(' ');
                if (accountEnd > 0)
                {
                    result.Account = input.Substring(1, accountEnd - 1);
                    input = input.Substring(accountEnd + 1);
                    parts = input.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                }
            }

            if (parts.Length > 0)
            {
                result.Name = parts[0].Trim();
            }

            return result;
        }

        #endregion
    }

    /// <summary>
    /// Representa información de moneda y monto en campos SWIFT (32A, 33B).
    /// </summary>
    public class CurrencyAmount
    {
        #region Propiedades

        /// <summary>
        /// Fecha de valor (YYMMDD).
        /// </summary>
        public DateTime? ValueDate { get; set; }

        /// <summary>
        /// Código de moneda (USD, EUR, etc.)
        /// </summary>
        public string Currency { get; set; }

        /// <summary>
        /// Monto de la transacción
        /// </summary>
        public decimal Amount { get; set; }

        #endregion

        #region Métodos públicos

        /// <summary>
        /// Parsea una cadena de moneda/monto en formato SWIFT
        /// </summary>
        /// <param name="input">Cadena en formato (YYMMDDCURRENCYAMOUNT).</param>
        /// <returns>Objeto CurrencyAmount parseado</returns>
        public static CurrencyAmount Parse(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                throw new ArgumentException("La entrada no puede estar vacía", nameof(input));
            }

            var result = new CurrencyAmount();

            // Ejemplo: 250709USD6400,
            if (input.Length >= 6)
            {
                if (DateTime.TryParseExact(input.Substring(0, 6), "yyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    result.ValueDate = date;
                }
            }

            if (input.Length >= 9)
            {
                result.Currency = input.Substring(6, 3);

                if (decimal.TryParse(input.Substring(9).Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var amount))
                {
                    result.Amount = amount;
                }
            }

            return result;
        }

        #endregion
    }

    /// <summary>
    /// Represents the trailer block of a SWIFT message.
    /// </summary>
    public class Trailer
    {
        /// <summary>
        /// Gets or sets the message authentication code (MAC).
        /// </summary>
        public string MessageAuthenticationCode { get; set; }

        /// <summary>
        /// Gets or sets the checksum (CHK).
        /// </summary>
        public string Checksum { get; set; }
    }
}
