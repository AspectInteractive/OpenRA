#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System.Collections.Generic;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Orders;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Orders
{
	public class UnitOrderGenerator : IOrderGenerator
	{
		readonly string worldSelectCursor = ChromeMetrics.Get<string>("WorldSelectCursor");
		readonly string worldDefaultCursor = ChromeMetrics.Get<string>("WorldDefaultCursor");

		protected static Target TargetForInput(World world, CPos cell, int2 worldPixel, MouseInput mi)
		{
			var worldRenderer = world.ScreenMap.GetWorldRenderer();
			var terrainPosCell = worldRenderer.Viewport.ViewToWorld(mi.Location);
			var terrainPos = worldRenderer.Viewport.ViewToWorldPxWithCellZ(mi.Location);
			/*var terrainPos = worldRenderer.ProjectedPosition(worldRenderer.Viewport.ViewToWorldPx(mi.Location));*/
			var actor = world.ScreenMap.ActorsAtMouse(mi)
				.Where(a => !a.Actor.IsDead && a.Actor.Info.HasTraitInfo<ITargetableInfo>() && !world.FogObscures(a.Actor))
				.WithHighestSelectionPriority(worldPixel, mi.Modifiers);

			if (actor != null)
				return Target.FromActorWithTerrainPos(actor, terrainPos);

			var frozen = world.ScreenMap.FrozenActorsAtMouse(world.RenderPlayer, mi)
				.Where(a => a.Info.HasTraitInfo<ITargetableInfo>() && a.Visible && a.HasRenderables)
				.WithHighestSelectionPriority(worldPixel, mi.Modifiers);

			if (frozen != null)
				return Target.FromFrozenActorWithTerrainPos(frozen, terrainPos);

			#if DEBUG
			// System.Console.WriteLine($"terrainPos: {terrainPos}, world.Map.CenterOfCell(terrainPosCell): {world.Map.CenterOfCell(terrainPosCell)}");
			#endif
			return Target.FromCellWithTerrainPos(cell, SubCell.FullCell, terrainPos); // use world.Map.CenterOfCell(cell) instead of terrainPos if this is needed
		}

		public virtual IEnumerable<Order> Order(World world, CPos cell, int2 worldPixel, MouseInput mi)
		{
			var target = TargetForInput(world, cell, worldPixel, mi);
			#if DEBUG
			System.Console.WriteLine($"Order issued with targCenterPos {target.CenterPosition} at {(System.DateTime.Now.Ticks / System.TimeSpan.TicksPerMillisecond)}");
			#endif
			var actorsAt = world.ActorMap.GetActorsAt(cell).ToList();
			var orders = world.Selection.Actors
				.Select(a => OrderForUnit(a, target, cell, mi))
				.Where(o => o != null)
				.ToList();

			var actorList = world.Selection.Actors.ToList();

			var actorsInvolved = orders.Select(o => o.Actor).Distinct().ToArray();
			if (actorsInvolved.Length == 0)
				yield break;

			// HACK: This is required by the hacky player actions-per-minute calculation
			// TODO: Reimplement APM properly and then remove this
			yield return new Order("CreateGroup", actorsInvolved[0].Owner.PlayerActor, false, actorsInvolved);

			foreach (var o in orders)
				if (!o.Trait.Orders.Where(oo => oo.OrderID == "Move").Any())
					yield return CheckSameOrder(o.Order, o.Trait.IssueOrder(o.Actor, o.Order, o.Target, mi.Modifiers.HasModifier(Modifiers.Shift)));
				else
					yield return new Order("Move", null, o.Target, mi.Modifiers.HasModifier(Modifiers.Shift), null, actorList.ToArray());
		}

		public virtual void Tick(World world) { }
		public virtual IEnumerable<IRenderable> Render(WorldRenderer wr, World world) { yield break; }
		public virtual IEnumerable<IRenderable> RenderAboveShroud(WorldRenderer wr, World world) { yield break; }
		public virtual IEnumerable<IRenderable> RenderAnnotations(WorldRenderer wr, World world) { yield break; }

		public virtual string GetCursor(World world, CPos cell, int2 worldPixel, MouseInput mi)
		{
			var target = TargetForInput(world, cell, worldPixel, mi);

			bool useSelect;
			if (Game.Settings.Game.UseClassicMouseStyle && !InputOverridesSelection(world, worldPixel, mi))
				useSelect = target.Type == TargetType.Actor && target.Actor.Info.HasTraitInfo<ISelectableInfo>();
			else
			{
				var ordersWithCursor = world.Selection.Actors
					.Select(a => OrderForUnit(a, target, cell, mi))
					.Where(o => o != null && o.Cursor != null);

				var cursorOrder = ordersWithCursor.MaxByOrDefault(o => o.Order.OrderPriority);
				if (cursorOrder != null)
					return cursorOrder.Cursor;

				useSelect = target.Type == TargetType.Actor && target.Actor.Info.HasTraitInfo<ISelectableInfo>() &&
					(mi.Modifiers.HasModifier(Modifiers.Shift) || world.Selection.Actors.Count == 0);
			}

			return useSelect ? worldSelectCursor : worldDefaultCursor;
		}

		public void Deactivate() { }

		bool IOrderGenerator.HandleKeyPress(KeyInput e) { return false; }

		// Used for classic mouse orders, determines whether or not action at xy is move or select
		public virtual bool InputOverridesSelection(World world, int2 xy, MouseInput mi)
		{
			var actor = world.ScreenMap.ActorsAtMouse(xy)
				.Where(a => !a.Actor.IsDead && a.Actor.Info.HasTraitInfo<ISelectableInfo>() && (a.Actor.Owner.IsAlliedWith(world.RenderPlayer) || !world.FogObscures(a.Actor)))
				.WithHighestSelectionPriority(xy, mi.Modifiers);

			if (actor == null)
				return true;

			var target = Target.FromActor(actor);
			var cell = world.Map.CellContaining(target.CenterPosition);
			var actorsAt = world.ActorMap.GetActorsAt(cell).ToList();

			var modifiers = TargetModifiers.None;
			if (mi.Modifiers.HasModifier(Modifiers.Ctrl))
				modifiers |= TargetModifiers.ForceAttack;
			if (mi.Modifiers.HasModifier(Modifiers.Shift))
				modifiers |= TargetModifiers.ForceQueue;
			if (mi.Modifiers.HasModifier(Modifiers.Alt))
				modifiers |= TargetModifiers.ForceMove;

			foreach (var a in world.Selection.Actors)
			{
				var o = OrderForUnit(a, target, cell, mi);
				if (o != null && o.Order.TargetOverridesSelection(a, target, actorsAt, cell, modifiers))
					return true;
			}

			return false;
		}

		public virtual void SelectionChanged(World world, IEnumerable<Actor> selected) { }

		/// <summary>
		/// Returns the most appropriate order for a given actor and target.
		/// First priority is given to orders that interact with the given actors.
		/// Second priority is given to actors in the given cell.
		/// </summary>
		protected static UnitOrderResult OrderForUnit(Actor self, Target target, CPos xy, MouseInput mi)
		{
			if (mi.Button != Game.Settings.Game.MouseButtonPreference.Action)
				return null;

			if (self.Owner != self.World.LocalPlayer)
				return null;

			if (self.World.IsGameOver)
				return null;

			if (self.Disposed || !target.IsValidFor(self))
				return null;

			var modifiers = TargetModifiers.None;
			if (mi.Modifiers.HasModifier(Modifiers.Ctrl))
				modifiers |= TargetModifiers.ForceAttack;
			if (mi.Modifiers.HasModifier(Modifiers.Shift))
				modifiers |= TargetModifiers.ForceQueue;
			if (mi.Modifiers.HasModifier(Modifiers.Alt))
				modifiers |= TargetModifiers.ForceMove;

			// The Select(x => x) is required to work around an issue on mono 5.0
			// where calling OrderBy* on SelectManySingleSelectorIterator can in some
			// circumstances (which we were unable to identify) replace entries in the
			// enumeration with duplicates of other entries.
			// Other action that replace the SelectManySingleSelectorIterator with a
			// different enumerator type (e.g. .Where(true) or .ToList()) also work.

			var orders = self.TraitsImplementing<IIssueOrder>()
				.SelectMany(trait => trait.Orders.Select(x => new { Trait = trait, Order = x }))
				.Select(x => x)
				.OrderByDescending(x => x.Order.OrderPriority)
				.ToList();

			for (var i = 0; i < 2; i++)
			{
				foreach (var o in orders)
				{
					var localModifiers = modifiers;
					string cursor = null;
					if (o.Order.CanTarget(self, target, ref localModifiers, ref cursor))
						return new UnitOrderResult(self, o.Order, o.Trait, cursor, target);
				}

				// No valid orders, so check for orders against the cell
				target = Target.FromCellWithTerrainPos(xy);
			}

			return null;
		}

		static Order CheckSameOrder(IOrderTargeter iot, Order order)
		{
			if (order == null && iot.OrderID != null)
				TextNotificationsManager.Debug("BUG: in order targeter - decided on {0} but then didn't order", iot.OrderID);
			else if (order != null && iot.OrderID != order.OrderString)
				TextNotificationsManager.Debug("BUG: in order targeter - decided on {0} but ordered {1}", iot.OrderID, order.OrderString);
			return order;
		}

		protected sealed class UnitOrderResult
		{
			public readonly Actor Actor;
			public readonly IOrderTargeter Order;
			public readonly IIssueOrder Trait;
			public readonly string Cursor;
			public ref readonly Target Target => ref target;

			readonly Target target;

			public UnitOrderResult(Actor actor, IOrderTargeter order, IIssueOrder trait, string cursor, in Target target)
			{
				Actor = actor;
				Order = order;
				Trait = trait;
				Cursor = cursor;
				this.target = target;
			}
		}

		class UnitGroupOrderResult
		{
			public readonly List<Actor> Actors;
			public readonly IOrderTargeter Order;
			public readonly IIssueOrder Trait;
			public readonly string Cursor;
			public ref readonly Target Target => ref target;

			readonly Target target;

			public UnitGroupOrderResult(List<Actor> actors, IOrderTargeter order, IIssueOrder trait, string cursor, in Target target)
			{
				Actors = actors;
				Order = order;
				Trait = trait;
				Cursor = cursor;
				this.target = target;
			}
		}

		public virtual bool ClearSelectionOnLeftClick => true;
	}
}
