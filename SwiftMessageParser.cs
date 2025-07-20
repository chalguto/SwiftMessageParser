namespace Swift
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
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
                        this.ParseCurrencyAmount(tag.Value, out DateTime date, out string currency, out decimal amount);
                        message.TextBlock.ValueDate = date;
                        message.TextBlock.Currency = currency;
                        message.TextBlock.Amount = amount;
                        break;
                    case "33B":
                        this.ParseCurrencyAmount(tag.Value, out _, out string currency33B, out decimal amount33B);
                        message.TextBlock.Currency33B = currency33B;
                        message.TextBlock.Amount33B = amount33B;
                        break;
                    case "50K":
                        message.TextBlock.OrderingCustomer = tag.Value;
                        break;
                    case "52A":
                        message.TextBlock.OrderingInstitution = tag.Value;
                        break;
                    case "59":
                        message.TextBlock.Beneficiary = tag.Value;
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

        private void ParseCurrencyAmount(string value, out DateTime date, out string currency, out decimal amount)
        {
            date = DateTime.MinValue;
            currency = string.Empty;
            amount = 0m;

            if (value.Length < 11)
            {
                return;
            }

            try
            {
                // Formato: YYMMDDCURRENCYAMOUNT
                int year = 2000 + int.Parse(value.Substring(0, 2));
                int month = int.Parse(value.Substring(2, 2));
                int day = int.Parse(value.Substring(4, 2));
                date = new DateTime(year, month, day);

                currency = value.Substring(6, 3);
                string amountStr = value.Substring(9).Replace(",", ".");

                if (decimal.TryParse(amountStr, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal parsedAmount))
                {
                    amount = parsedAmount;
                }
            }
            catch
            {
                // En caso de error, dejamos los valores por defecto
            }
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
        /// Gets or sets the value date (part of :32A:).
        /// </summary>
        public DateTime ValueDate { get; set; }

        /// <summary>
        /// Gets or sets the currency (part of :32A:).
        /// </summary>
        public string Currency { get; set; }

        /// <summary>
        /// Gets or sets the amount (part of :32A:).
        /// </summary>
        public decimal Amount { get; set; }

        /// <summary>
        /// Gets or sets the currency for tag :33B:.
        /// </summary>
        public string Currency33B { get; set; }

        /// <summary>
        /// Gets or sets the amount for tag :33B:.
        /// </summary>
        public decimal Amount33B { get; set; }

        /// <summary>
        /// Gets or sets the ordering customer (:50K:).
        /// </summary>
        public string OrderingCustomer { get; set; }

        /// <summary>
        /// Gets or sets the ordering institution (:52A:).
        /// </summary>
        public string OrderingInstitution { get; set; }

        /// <summary>
        /// Gets or sets the beneficiary (:59:).
        /// </summary>
        public string Beneficiary { get; set; }

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