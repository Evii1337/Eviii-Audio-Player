using System.ComponentModel;

namespace EviAudio.Other;

public class BotsList
{
    [Description("Display name of the bot NPC in-game.")]
    public string BotName { get; set; } = "Dedicated Server";

    [Description("Unique numeric ID used in all audio commands for this bot.")]
    public int BotId { get; set; } = 99;
}
