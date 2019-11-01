﻿using System;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GreenPipes;
using GreenPipes.Internals.Extensions;
using Marten;
using MassTransit.Contexts;
using MassTransit.Messages;
using MassTransit.Objects.Commands;
using MassTransit.Objects.Models;
using MassTransit.Payloads;
using MassTransit.Repositories;
using MassTransit.Testing;
using Moq;
using NUnit.Framework;
using static Moq.It;
using static Moq.Mock;

namespace MassTransit.Consumers
{
    [Category("Consumer, SmtpStorage")]
    [TestFixture]
    public sealed class CreateSmtpServerConsumer_Specs
    {
        InMemoryTestHarness _harness;

        Mock<ISmtpServersRepository> _smtpServersRepositoryMock;

        Mock<IDocumentSession> _documentSessionMock;

        CreateSmtpServer _createSmtpServer;

        CreateSmtpServerCommand _createSmtpServerCommand;

        const int _id = 1000;

        [SetUp]
        public async Task A_consumer_being_tested()
        {
            _smtpServersRepositoryMock = new Mock<ISmtpServersRepository>();
            _smtpServersRepositoryMock
                .Setup(x => x.SendCommand(IsAny<PipeContext>(), IsAny<CreateSmtpServerCommand>(), IsAny<CancellationToken>()))
                .Callback<PipeContext, CreateSmtpServerCommand, CancellationToken>((context, command, __) =>
                {
                    _createSmtpServerCommand = command;

                    var identity = context.GetOrAddPayload(() => new Identity<SmtpServer, int>(_id));
                })
                .Returns(Task.CompletedTask);

            _documentSessionMock = new Mock<IDocumentSession>();

            var documentStoreContext = Of<DocumentStoreContext>(x =>
                x.LightweightSession(IsAny<string>(), IsAny<IsolationLevel>()) == new ValueTask<IDocumentSession>(_documentSessionMock.Object) &&
                x.OpenSession(IsAny<string>(), IsAny<IsolationLevel>()) == new ValueTask<IDocumentSession>(_documentSessionMock.Object));

            _harness = new InMemoryTestHarness { TestTimeout = TimeSpan.FromSeconds(2) };
            _harness.OnConfigureReceiveEndpoint += configurator =>
            {
                configurator.UseInlineFilter((context, next) =>
                {
                    context.GetOrAddPayload(() => documentStoreContext);

                    return next.Send(context);
                });
            };

            var sut = _harness.Consumer(() => new CreateSmtpServerConsumer(_smtpServersRepositoryMock.Object));

            _createSmtpServer = TypeCache<CreateSmtpServer>.InitializeFromObject(new
            {
                Name = Guid.NewGuid().ToString(),
                Host = "host.com",
                Port = 1000,
                UseSsl = true
            });

            await _harness.Start();

            await _harness.InputQueueSendEndpoint.Send(_createSmtpServer);
        }

        [TearDown]
        public async Task Before_each()
        {
            _smtpServersRepositoryMock.Reset();

            await _harness.Stop();
        }

        [Test]
        public void Should_store_smtp_server_once()
        {
            //
            var consumed = _harness.Consumed.Select<CreateSmtpServer>().Single();

            //
            _createSmtpServerCommand.Should().BeEquivalentTo(_createSmtpServer);
        }

        [Test]
        public void Should_publish_smtp_server_created()
        {
            //
            var published = _harness.Published.Select<SmtpServerCreated>().Single();

            //
            published.Context.Message.Id.Should().Be(_id);
        }
    }
}
