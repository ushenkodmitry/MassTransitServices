﻿using GreenPipes;
using MassTransit.SmtpGateway.Messages;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MassTransit.SmtpGateway
{
    sealed class SmtpGatewayConnector : ISmtpGatewayConnector
    {
        readonly IPublishEndpoint _publishEndpoint;

        readonly Guid? _correlationId;

        readonly CancellationToken _cancellationToken;

        public SmtpGatewayConnector(IPublishEndpoint publishEndpoint, Guid? correlationId = null, CancellationToken cancellationToken = default)
        {
            _publishEndpoint = publishEndpoint;
            _correlationId = correlationId;
            _cancellationToken = cancellationToken;
        }

        public async Task SendMail(Action<ISendBuilder> build, CancellationToken cancellationToken = default)
        {
            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationToken, cancellationToken);

            SendBuilder builder = new SendBuilder();

            if (_correlationId.HasValue)
                builder.WithCorrelationId(_correlationId.Value);

            build(builder);

            SendMail sendMail = builder.Build();

            await _publishEndpoint.Publish(sendMail, cts.Token).ConfigureAwait(false);
        }

        public async Task SendMail(Action<ISendBuilder> build, IPipe<PublishContext<SendMail>> pipe, CancellationToken cancellationToken = default)
        {
            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationToken, cancellationToken);

            SendBuilder builder = new SendBuilder();

            if (_correlationId.HasValue)
                builder.WithCorrelationId(_correlationId.Value);

            build(builder);

            SendMail sendMail = builder.Build();

            await _publishEndpoint.Publish(sendMail, pipe, cts.Token).ConfigureAwait(false);
        }

        sealed class SendBuilder : ISendBuilder
        {
            string _subject;

            string _messageId;

            string _routingKey;

            Guid _correlationId;

            readonly MailboxesBuilder _from;

            readonly MailboxesBuilder _to;

            readonly Lazy<MailboxesBuilder> _cc;

            readonly Lazy<MailboxesBuilder> _bcc;

            readonly Lazy<HeadersBuilder> _withHeaders;

            readonly Lazy<AttachmentsBuilder> _withAttachments;

            readonly Lazy<BodyBuilder> _withBody;

            readonly Lazy<ImportanceSelector> _withImportance;
            public IImportanceSelector WithImportance => _withImportance.Value;

            readonly Lazy<PrioritySelector> _withPriority;
            public IPrioritySelector WithPriority => _withPriority.Value;

            readonly Lazy<XPrioritySelector> _withXPriority;
            public IXPrioritySelector WithXPriority => _withXPriority.Value;

            public SendBuilder()
            {
                _from = new MailboxesBuilder();
                _to = new MailboxesBuilder();
                _cc = new Lazy<MailboxesBuilder>(() => new MailboxesBuilder());
                _bcc = new Lazy<MailboxesBuilder>(() => new MailboxesBuilder());
                _withHeaders = new Lazy<HeadersBuilder>(() => new HeadersBuilder());
                _withImportance = new Lazy<ImportanceSelector>(() => new ImportanceSelector(this));
                _withPriority = new Lazy<PrioritySelector>(() => new PrioritySelector(this));
                _withXPriority = new Lazy<XPrioritySelector>(() => new XPrioritySelector(this));
                _withAttachments = new Lazy<AttachmentsBuilder>(() => new AttachmentsBuilder());
                _withBody = new Lazy<BodyBuilder>(() => new BodyBuilder());
            }

            public ISendBuilder WithCorrelationId(Guid correlationId)
            {
                _correlationId = correlationId;

                return this;
            }

            public ISendBuilder WithSubject(string subject)
            {
                _subject = subject;

                return this;
            }

            public ISendBuilder WithRoutingKey(string routingKey)
            {
                _routingKey = routingKey;

                return this;
            }

            public ISendBuilder WithHeaders(Action<IHeadersBuilder> headers)
            {
                headers(_withHeaders.Value);

                return this;
            }

            public ISendBuilder WithMessageId(string messageId)
            {
                _messageId = messageId;

                return this;
            }

            public ISendBuilder WithAttachments(Action<IAttachmentsBuilder> attachments)
            {
                attachments(_withAttachments.Value);

                return this;
            }

            public ISendBuilder From(Action<IMailboxesBuilder> from)
            {
                from(_from);

                return this;
            }

            public ISendBuilder To(Action<IMailboxesBuilder> to)
            {
                to(_to);

                return this;
            }

            public ISendBuilder Cc(Action<IMailboxesBuilder> cc)
            {
                cc(_cc.Value);

                return this;
            }

            public ISendBuilder Bcc(Action<IMailboxesBuilder> bcc)
            {
                bcc(_bcc.Value);

                return this;
            }

            public SendMail Build()
            {
                static string[] ToMailboxes(MailboxesBuilder builder) => builder.Mailboxes.Select(m => $"{m.Name}{ASCII.UnitSeparator}{m.Address}").ToArray();

                var sendMail = new SendMailMessage
                {
                    Subject = _subject,
                    From = ToMailboxes(_from),
                    To = ToMailboxes(_to),
                    MessageId = _messageId
                };

                void BuildAttachments(AttachmentsBuilder builder)
                {
                    IList<byte[]> buffers = new List<byte[]>();
                    IList<string> attachmentMeta = new List<string>();

                    int offset = 0;

                    foreach (var attachment in builder.Attachments)
                    {
                        int length = (int)attachment.Stream.Length;

                        var buffer = new byte[length];
                        if(attachment.Stream.CanSeek)
                            attachment.Stream.Seek(0, SeekOrigin.Begin);
                        int read = attachment.Stream.Read(buffer, 0, length);

                        buffers.Add(buffer);
                        attachmentMeta.Add($"{attachment.FileName}{ASCII.UnitSeparator}{attachment.MediaType}{ASCII.UnitSeparator}{attachment.MediaSubType}{ASCII.UnitSeparator}{offset}{ASCII.UnitSeparator}{length}");

                        offset += length;
                    }

                    sendMail.AttachmentsContent = buffers.SelectMany(b => b).ToArray();
                    sendMail.AttachmentsMeta = attachmentMeta.ToArray();
                }

                if (_withImportance.IsValueCreated)
                    sendMail.Importance = _withImportance.Value.Importance;
                if (_withXPriority.IsValueCreated)
                    sendMail.XPriority = _withXPriority.Value.Priority;
                if (_withPriority.IsValueCreated)
                    sendMail.Priority = _withPriority.Value.Priority;
                if (_cc.IsValueCreated && _cc.Value.Mailboxes.Count > 0)
                    sendMail.Cc = ToMailboxes(_cc.Value);
                if (_bcc.IsValueCreated && _bcc.Value.Mailboxes.Count > 0)
                    sendMail.Bcc = ToMailboxes(_bcc.Value);
                if (_withAttachments.IsValueCreated)
                    BuildAttachments(_withAttachments.Value);
                if(_withBody.IsValueCreated)
                {
                    if (_withBody.Value.IsHtmlBody)
                        sendMail.HtmlBody = _withBody.Value.Body;
                    sendMail.TextBody = _withBody.Value.Body;
                }

                return sendMail;
            }

            public ISendBuilder WithBody(Action<IBodyBuilder> body)
            {
                body(_withBody.Value);

                return this;
            }

            sealed class SendMailMessage : SendMail
            {
                public Guid CorrelationId { get; set; }
                public string Subject { get; set; }
                public string Importance { get; set; }
                public string Priority { get; set; }
                public string XPriority { get; set; }
                public string TextBody { get; set; }
                public string HtmlBody { get; set; }
                public string MessageId { get; set; }
                public string[] To { get; set; }
                public string[] Bcc { get; set; }
                public string[] Cc { get; set; }
                public string[] From { get; set; }
                public string[] Headers { get; set; }
                public string[] AttachmentsMeta { get; set; }
                public byte[] AttachmentsContent { get; set; }
                public string RoutingKey { get; set; }
            }

            sealed class ImportanceSelector : IImportanceSelector
            {
                readonly ISendBuilder _builder;

                public string Importance { get; private set; }

                public ImportanceSelector(ISendBuilder builder)
                {
                    _builder = builder;

                    _builder = Normal();
                }

                public ISendBuilder Normal()
                {
                    Importance = "Normal";

                    return _builder;
                }

                public ISendBuilder Low()
                {
                    Importance = "Low";

                    return _builder;
                }

                public ISendBuilder High()
                {
                    Importance = "High";

                    return _builder;
                }
            }

            sealed class PrioritySelector : IPrioritySelector
            {
                readonly ISendBuilder _builder;

                public string Priority { get; private set; }

                public PrioritySelector(ISendBuilder builder)
                {
                    _builder = builder;

                    _builder = Normal();
                }

                public ISendBuilder Normal()
                {
                    Priority = "Normal";

                    return _builder;
                }

                public ISendBuilder NonUrgent()
                {
                    Priority = "NonUrgent";

                    return _builder;
                }

                public ISendBuilder Urgent()
                {
                    Priority = "Urgent";

                    return _builder;
                }
            }

            sealed class XPrioritySelector : IXPrioritySelector
            {
                readonly ISendBuilder _builder;

                public string Priority { get; private set; }

                public XPrioritySelector(ISendBuilder builder)
                {
                    _builder = builder;

                    _builder = Normal();
                }

                public ISendBuilder Normal()
                {
                    Priority = "Normal";

                    return _builder;
                }

                public ISendBuilder Low()
                {
                    Priority = "Low";

                    return _builder;
                }

                public ISendBuilder Lowest()
                {
                    Priority = "Lowest";

                    return _builder;
                }

                public ISendBuilder High()
                {
                    Priority = "High";

                    return _builder;
                }

                public ISendBuilder Highest()
                {
                    Priority = "Highest";

                    return _builder;
                }
            }

            sealed class MailboxesBuilder : IMailboxesBuilder
            {
                public IList<(string Name, string Address)> Mailboxes { get; } = new List<(string Name, string Address)>();

                public IMailboxesBuilder Mailbox(string name, string address)
                {
                    Mailboxes.Add((name, address));

                    return this;
                }
            }

            sealed class HeadersBuilder : IHeadersBuilder
            {
                public IList<(string Field, string Value, Encoding Encoding)> Headers { get; } = new List<(string Field, string Value, Encoding Encoding)>();

                public IHeadersBuilder Header(string field, string value, Encoding encoding = null)
                {
                    Headers.Add((field, value, encoding ?? Encoding.UTF8));

                    return this;
                }
            }

            sealed class AttachmentsBuilder : IAttachmentsBuilder
            {
                public IList<(string FileName, Stream Stream, string MediaType, string MediaSubType)> Attachments { get; } = new List<(string FileName, Stream stream, string MediaType, string MediaSubType)>();

                public IAttachmentsBuilder Attach(string fileName, Stream stream, string mediaType = null, string mediaSubType = null)
                {
                    Attachments.Add((fileName, stream, mediaType, mediaSubType));

                    return this;
                }
            }

            sealed class BodyBuilder : IBodyBuilder
            {
                public bool IsHtmlBody { get; private set; }

                public string Body { get; set; }

                public IBodyBuilder HtmlBody(string body)
                {
                    Body = body;
                    IsHtmlBody = true;

                    return this;
                }

                public IBodyBuilder TextBody(string body)
                {
                    Body = body;

                    return this;
                }
            }
        }
    }
}
