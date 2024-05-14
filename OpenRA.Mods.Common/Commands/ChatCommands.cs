#region Copyright & License Information
/*
 * Copyright 2007-2022 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System.Collections.Generic;
using System.Linq;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Commands
{
	public readonly struct Command
	{
		public readonly string Name;
		public readonly string Desc;
		public readonly bool InHelp;

		public Command(string name, string desc, bool inHelp)
		{
			Name = name;
			Desc = desc;
			InHelp = inHelp;
		}
	}

	[TraitLocation(SystemActors.World)]
	[Desc("Enables commands triggered by typing them into the chatbox. Attach this to the world actor.")]
	public class ChatCommandsInfo : TraitInfo<ChatCommands> { }

	public class ChatCommands : INotifyChat
	{
		public Dictionary<string, List<IChatCommand>> Commands { get; }

		public ChatCommands()
		{
			Commands = new Dictionary<string, List<IChatCommand>>();
		}

		public bool OnChat(string playername, string message)
		{
			if (message.StartsWith("/"))
			{
				var name = message.Substring(1).Split(' ')[0].ToLowerInvariant();
				var commandList = Commands.FirstOrDefault(x => x.Key == name);

				if (commandList.Value != null)
					foreach (var command in commandList.Value)
						command.InvokeCommand(name.ToLowerInvariant(), message.Substring(1 + name.Length).Trim());
				else
					TextNotificationsManager.Debug("{0} is not a valid command.", name);

				return false;
			}

			return true;
		}

		public void RegisterCommand(string name, IChatCommand command)
		{
			// Override possible duplicates instead of crashing.
			if (Commands.ContainsKey(name.ToLowerInvariant()))
				Commands[name.ToLowerInvariant()].Add(command);
			else
				Commands[name.ToLowerInvariant()] = new List<IChatCommand>() { command };
		}
	}

	public interface IChatCommand
	{
		void InvokeCommand(string command, string arg);
	}
}
