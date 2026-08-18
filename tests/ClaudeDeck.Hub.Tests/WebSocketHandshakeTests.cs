namespace ClaudeDeck.Hub.Tests;

public class WebSocketHandshakeTests
{
    [Fact]
    public void The_accept_key_matches_the_worked_example_in_the_specification()
    {
        // RFC 6455 section 1.3. Getting this wrong means no client ever completes the upgrade.
        Assert.Equal("s3pPLMBiTxaQ9kYGzzhZRbK+xOo=", WebSocketHandshake.AcceptKey("dGhlIHNhbXBsZSBub25jZQ=="));
    }
}
