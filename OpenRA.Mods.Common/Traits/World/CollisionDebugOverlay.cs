#region Copyright & License Information
/*
 * Copyright 2007-2021 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Commands;
using OpenRA.Mods.Common.Graphics;
using OpenRA.Primitives;
using OpenRA.Traits;
using RVO;
using static System.Net.Mime.MediaTypeNames;
using static OpenRA.Mods.Common.Pathfinder.ThetaStarPathSearch;
using static OpenRA.Mods.Common.Traits.CollisionDebugOverlay;
using static OpenRA.Mods.Common.Traits.MobileOffGridOverlay;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.World | SystemActors.EditorWorld)]
	[Desc("Renders a debug overlay of additional collision debug traits")]
	public class CollisionDebugOverlayInfo : TraitInfo<CollisionDebugOverlay> { }

	public class CollisionDebugOverlay : IWorldLoaded, IChatCommand
	{
		public readonly struct BCDCellNode
		{
			public readonly int ID;
			public readonly CPos NodeCPos;
			public readonly WPos NodePos;
			public readonly Color LineColor;
			public readonly Color TextColor;
			public readonly int LineThickness;
			public readonly LineEndPoint LineEnd;
			public readonly string FontName;

			public BCDCellNode(World world, int id, DomainNode<CPos> node, bool blocked,
				int lineThickness = 3, string fontName = "MediumBold", LineEndPoint lineEndPoint = LineEndPoint.EndArrow)
			{
				ID = id;
				NodeCPos = node.Value;
				NodePos = world.Map.CenterOfCell(node.Value);
				LineColor = blocked ? Color.Red : Color.Green;
				TextColor = blocked ? Color.Red : Color.Green;
				LineThickness = lineThickness;
				FontName = fontName;
				LineEnd = lineEndPoint;
			}

			public readonly WPos LineEndPos => NodePos;
			public readonly WPos LineStartPos => NodePos;
			public readonly string Text => $"ID:{ID}";
		}

		World world;
		public readonly List<Command> Comms;
		(BCDCellNode Node, List<IRenderable> Annos)[,] bcdCellNodes;
		readonly List<(List<WPos>, Color, List<LineAnnotationRenderableWithZIndex> Anno)> cellEdges = new();
		readonly List<(List<WPos>, Color, int, LineEndPoint EndPoints, List<LineAnnotationRenderableWithZIndex> Anno)> linesWithColorsAndThickness = new();
		readonly List<((WPos, WDist), Color, int, CircleAnnotationRenderable Anno)> circlesWithColors = new();
		readonly List<(WPos, Color, int, CircleAnnotationRenderable Anno)> pointsWithColors = new();
		readonly List<(List<WPos> Path, Color? C, List<LineAnnotationRenderableWithZIndex> Anno)> paths = new();
		readonly List<(Actor, WPos, Color, int, CircleAnnotationRenderable Anno)> actorPointsWithColors = new();
		List<(CCState, Color, (CircleAnnotationRenderable PAnno, TextAnnotationRenderable TAnno) Annos)> statesWithColors = new();
		readonly List<(WPos, string, Color, string, TextAnnotationRenderable Anno)> textsWithColors = new();
		readonly List<(Actor, WPos, string, Color, string, TextAnnotationRenderable Anno)> actorTextsWithColors = new();
		readonly List<(List<WPos>, int, List<LineAnnotationRenderableWithZIndex> Anno)> linesWithThickness = new();

		// Set this to true to display annotations showing the cost at each cell
		readonly bool showCosts = true;

		public bool Enabled;
		const int DefaultThickness = 3;
		const int DefaultArrowThickness = 3;
		const int DefaultSharpnessDegrees = 45; // angle in degrees that each side of the arrow head of an arrow should be
		const int LineThickness = 3;
		readonly WDist defaultArrowLength = new(256);
		float currHue = Color.Blue.ToAhsv().H; // 0.0 - 1.0
		readonly float pointHue = Color.Red.ToAhsv().H; // 0.0 - 1.0
		readonly float circleHue = Color.LightGreen.ToAhsv().H; // 0.0 - 1.0
		readonly float pathHue = Color.Yellow.ToAhsv().H; // 0.0 - 1.0
		readonly float lineHue = Color.LightBlue.ToAhsv().H; // 0.0 - 1.0
		readonly float currSat = 1.0F; // 0.0 - 1.0
		readonly float currLight = 0.7F; // 0.0 - 1.0 with 1.0 being brightest
		float lineColorIncrement = 0.05F;
		readonly int pointRadius = 100;
		const int EndPointRadius = 100;
		const int EndPointThickness = 3;
		readonly string defaultFontName = "TinyBold";
		readonly SpriteFont font = Game.Renderer.Fonts["TinyBold"];
		Color lineColor;
		public Action<string> ToggleVisibility;

		public CollisionDebugOverlay()
		{
			Comms = new List<Command>()
			{
				new("colldebug", "toggles the collision debug overlay.", true)
			};
		}

		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			world = w;
			bcdCellNodes = new (BCDCellNode Node, List<IRenderable> Annos)[w.Map.MapSize.X, w.Map.MapSize.Y];
			var console = w.WorldActor.TraitOrDefault<ChatCommands>();
			var help = w.WorldActor.TraitOrDefault<HelpCommand>();

			if (console == null || help == null)
				return;

			foreach (var comm in Comms)
			{
				console.RegisterCommand(comm.Name, this);
				if (comm.InHelp)
					help.RegisterHelp(comm.Name, comm.Desc);
			}

			ToggleVisibility = arg => DevCommands.Visibility(arg, w);
		}

		void IChatCommand.InvokeCommand(string name, string arg)
		{
			if (Comms.Any(comm => comm.Name == name))
			{
				Enabled ^= true;
				ToggleVisibility("");
			}

			var annos
				= linesWithColorsAndThickness.SelectMany(x => x.Anno).Cast<IRenderable>()
					.Union(linesWithThickness.SelectMany(x => x.Anno).Cast<IRenderable>())
					.Union(paths.SelectMany(x => x.Anno).Cast<IRenderable>())
					.Union(circlesWithColors.ConvertAll(x => (IRenderable)x.Anno))
					.Union(pointsWithColors.ConvertAll(x => (IRenderable)x.Anno))
					.Union(actorPointsWithColors.ConvertAll(x => (IRenderable)x.Anno))
					.Union(statesWithColors.SelectMany(x => new List<IRenderable>() { x.Annos.PAnno, x.Annos.TAnno }))
					.Union(textsWithColors.ConvertAll(x => (IRenderable)x.Anno))
					.Union(actorTextsWithColors.ConvertAll(x => (IRenderable)x.Anno))
					.Union(cellEdges.SelectMany(x => x.Anno).Cast<IRenderable>()).ToList();

			foreach (var (_, nodeAnnos) in bcdCellNodes)
				annos.AddRange(nodeAnnos);

			if (Enabled)
				foreach (var anno in annos)
					anno.AddOrUpdateScreenMap();
			else
				foreach (var anno in annos)
					anno.Dispose();
		}

		public static void GenericLinkedPointsFunc<T1>(List<T1> pointList, int pointListLen, Action<T1, T1> funcOnLinkedPoints)
		{
			for (var i = 0; i < pointListLen - 1; i++)
			{
				var currItem = pointList[i];
				var nextItem = pointList[i + 1];
				funcOnLinkedPoints(currItem, nextItem);
			}
		}

		public static void GenericLinkedPointsFunc<T1, T2>(List<T1> pointList, int pointListLen, Func<T1, T2> pointUnpacker,
														Action<T2, T2> funcOnLinkedPoints)
		{
			for (var i = 0; i < pointListLen - 1; i++)
			{
				var currItem = pointUnpacker(pointList[i]);
				var nextItem = pointUnpacker(pointList[i + 1]);
				funcOnLinkedPoints(currItem, nextItem);
			}
		}

		public static List<LineAnnotationRenderableWithZIndex> GetPathRenderableSet(World w, List<WPos> path,
			int lineThickness, Color lineColor, int endPointRadius, int endPointThickness, Color endPointColor)
		{
			var linesToRender = new List<LineAnnotationRenderableWithZIndex>();
			void FuncOnLinkedPoints(WPos wpos1, WPos wpos2) => linesToRender.Add(new LineAnnotationRenderableWithZIndex(w, wpos1, wpos2,
																			lineThickness, lineColor, lineColor,
																			(endPointRadius, endPointThickness, endPointColor)));
			GenericLinkedPointsFunc(path, path.Count, FuncOnLinkedPoints);
			return linesToRender;
		}
		static List<LineAnnotationRenderableWithZIndex> RenderArrowhead(World world, WPos start, WVec directionAndLength, Color color,
			int thickness = DefaultArrowThickness, int sharpnessDegrees = DefaultSharpnessDegrees)
		{
			var linesToRender = new List<LineAnnotationRenderableWithZIndex>();
			var end = start - directionAndLength;
			var endOfLeftSide = start + directionAndLength.Rotate(WRot.FromYaw(WAngle.FromDegrees(sharpnessDegrees + 180)));
			var endOfRightSide = start + directionAndLength.Rotate(WRot.FromYaw(WAngle.FromDegrees(-sharpnessDegrees + 180)));

			linesToRender.Add(new(world, start, end, thickness, color));
			linesToRender.Add(new(world, start, endOfLeftSide, thickness, color));
			linesToRender.Add(new(world, start, endOfRightSide, thickness, color));

			return linesToRender;
		}

		CircleAnnotationRenderable PointRenderFunc(WPos p, Color color, int thickness = DefaultThickness) =>
			new(world, p, new WDist(pointRadius), thickness, color, true);

		CircleAnnotationRenderable CircleRenderFunc((WPos, WDist) c, Color color, int thickness = DefaultThickness) =>
			new(world, c.Item1, c.Item2, thickness, color, false);

		TextAnnotationRenderable TextRenderFunc(WPos p, string text, Color color, string fontname)
		{
			fontname ??= defaultFontName;
			var font = Game.Renderer.Fonts[fontname];
			return new(world, font, p, 0, color, text);
		}

		List<LineAnnotationRenderableWithZIndex> LineRenderFunc(WPos start, WPos end, Color color,
			LineEndPoint endPoints = LineEndPoint.None, int thickness = DefaultThickness)
		{
			var linesToRender = new List<LineAnnotationRenderableWithZIndex>();

			if (endPoints == LineEndPoint.None)
				linesToRender.Add(new(world, start, end, thickness, color, color, (0, 0, color)));
			else if (endPoints == LineEndPoint.Circle)
				linesToRender.Add(new(world, start, end, thickness, color, color, (EndPointRadius, EndPointThickness, color)));
			else if (endPoints == LineEndPoint.StartArrow || endPoints == LineEndPoint.EndArrow || endPoints == LineEndPoint.BothArrows)
			{
				var arrowVec = new WVec(defaultArrowLength, WRot.FromYaw((end - start).Yaw));
				linesToRender.Add(new(world, start, end, thickness, color, color, (0, 0, color)));
				if (endPoints == LineEndPoint.StartArrow || endPoints == LineEndPoint.BothArrows)
					linesToRender = linesToRender.Union(RenderArrowhead(world, start, -arrowVec, color)).ToList();
				if (endPoints == LineEndPoint.EndArrow || endPoints == LineEndPoint.BothArrows)
					linesToRender = linesToRender.Union(RenderArrowhead(world, end, arrowVec, color)).ToList();
			}

			return linesToRender;
		}

		public void UpdatePointColors()
		{
			var newPointsWithColor = new List<(CCState, Color, (CircleAnnotationRenderable PAnno, TextAnnotationRenderable TAnno))>();
			currHue = 0.0F;
			var currColor = Color.FromAhsv(currHue, currSat, currLight);
			lineColorIncrement = 1.0F / statesWithColors.Count;

			for (var i = 0; i < statesWithColors.Count; i++)
			{
				newPointsWithColor.Add((statesWithColors[i].Item1, currColor,
					(PointRenderFunc(world.Map.WPosFromCCPos(statesWithColors[i].Item1.CC), Color.FromAhsv(currHue, currSat, currLight)),
					 new TextAnnotationRenderable(world, font, world.Map.WPosFromCCPos(statesWithColors[i].Item1.CC), 0,
						Color.FromAhsv(currHue, currSat, currLight), $"({statesWithColors[i].Item1.Gval})"))));
				currHue = (currHue + lineColorIncrement) % (1.0F + float.Epsilon);
				currColor = Color.FromAhsv(currHue, currSat, currLight);
			}

			statesWithColors = newPointsWithColor;
		}

		public void AddState(CCState ccState)
		{
			statesWithColors.Add((ccState, Color.FromAhsv(currHue, currSat, currLight),
				(PointRenderFunc(world.Map.WPosFromCCPos(ccState.CC), Color.FromAhsv(currHue, currSat, currLight)),
				 new TextAnnotationRenderable(world, font, world.Map.WPosFromCCPos(ccState.CC), 0,
					Color.FromAhsv(currHue, currSat, currLight), $"({ccState.Gval})"))));
			UpdatePointColors();
		}

		public void RemoveState(CCState ccState)
		{
			statesWithColors.RemoveAll(stateWithColor => stateWithColor.Item1 == ccState);
			UpdatePointColors();
		}

		public void AddPoint(WPos pos)
		{
			pointsWithColors.Add((pos, Color.FromAhsv(pointHue, currSat, currLight), DefaultThickness,
				PointRenderFunc(pos, Color.FromAhsv(pointHue, currSat, currLight), DefaultThickness)));
			UpdatePointColors();
		}

		public void AddPoint(WPos pos, int thickness = DefaultThickness)
		{
			pointsWithColors.Add((pos, Color.FromAhsv(pointHue, currSat, currLight), thickness,
				PointRenderFunc(pos, Color.FromAhsv(pointHue, currSat, currLight), thickness)));
			UpdatePointColors();
		}

		public void AddPoint(WPos pos, Color color, int thickness = DefaultThickness)
		{
			pointsWithColors.Add((pos, color, thickness, null));
			UpdatePointColors();
		}

		public void AddActorPoint(Actor self, WPos pos, int thickness = DefaultThickness)
		{
			actorPointsWithColors.Add((self, pos, Color.FromAhsv(pointHue, currSat, currLight), thickness,
				PointRenderFunc(pos, Color.FromAhsv(pointHue, currSat, currLight), thickness)));
		}

		public void AddActorPoint(Actor self, WPos pos, Color color, int thickness = DefaultThickness)
			=> actorPointsWithColors.Add((self, pos, color, thickness, PointRenderFunc(pos, color, thickness)));

		public void RemovePoint(WPos pos)
		{
			foreach (var (currPos, currColor, thickness, _) in pointsWithColors)
				if (currPos == pos)
					pointsWithColors.RemoveAll(pwc => pwc.Item1 == currPos && pwc.Item2 == currColor && pwc.Item3 == thickness);
			UpdatePointColors();
		}

		public void AddPath(List<WPos> path, Color? color = null)
		{
			paths.Add((path, color,
				GetPathRenderableSet(world, path, LineThickness, color ?? lineColor, EndPointRadius, EndPointThickness, lineColor)));
		}

		public void RemoveActorPoint(WPos pos) { actorPointsWithColors.RemoveAll(ap => ap.Item2 == pos); }
		public void RemoveActorPoint(Actor self) { actorPointsWithColors.RemoveAll(ap => ap.Item1 == self); }
		public void RemoveActorPoint(Actor self, WPos pos) { actorPointsWithColors.RemoveAll(ap => ap.Item1 == self && ap.Item2 == pos); }

		public void AddText(WPos pos, string text, Color color, string fontname = null)
			=> textsWithColors.Add((pos, text, color, fontname, TextRenderFunc(pos, text, color, fontname)));
		public void AddActorText(Actor self, WPos pos, string text, Color color, string fontname = null)
			=> actorTextsWithColors.Add((self, pos, text, color, fontname, TextRenderFunc(pos, text, color, fontname)));

		public void RemoveText(WPos pos, string text) { textsWithColors.RemoveAll(twc => twc.Item1 == pos && twc.Item2 == text); }

		public void RemoveActorText(Actor self) { actorTextsWithColors.RemoveAll(at => at.Item1 == self); }
		public void RemoveActorText(Actor self, WPos pos) { actorTextsWithColors.RemoveAll(at => at.Item1 == self && at.Item2 == pos); }
		public void RemoveActorText(Actor self, string text) { actorTextsWithColors.RemoveAll(at => at.Item1 == self && at.Item3 == text); }
		public void RemoveActorText(Actor self, WPos pos, string text) { actorTextsWithColors.RemoveAll(at => at.Item1 == self && at.Item2 == pos && at.Item3 == text); }
		public void RemovePath(List<WPos> path) { paths.RemoveAll(p => p.Path == path); }
		public void AddLine(WPos p1, WPos p2, int thickness = LineThickness) => AddLine(new List<WPos> { p1, p2 }, thickness);
		public void AddLine(List<WPos> line, int thickness = LineThickness)
		{
			linesWithThickness.Add((line, thickness,
				GetPathRenderableSet(world, line, thickness, lineColor, EndPointRadius, EndPointThickness, lineColor)));
		}

		public void RemoveLine(List<WPos> line) { linesWithThickness.RemoveAll(l => l.Item1 == line); }
		public void AddLineWithColor(List<WPos> line, Color color, int thickness = LineThickness, LineEndPoint endpoints = LineEndPoint.None)
		{
			linesWithColorsAndThickness.Add((line, color, thickness, endpoints,
				LineRenderFunc(line[0], line[1], color, endpoints, thickness)));
		}

		public void AddLineWithColor(WPos pos1, WPos pos2, Color color, int thickness = LineThickness, LineEndPoint endpoints = LineEndPoint.None)
		{
			linesWithColorsAndThickness.Add((new List<WPos>() { pos1, pos2 }, color, thickness, endpoints,
				LineRenderFunc(pos1, pos2, color, endpoints, thickness)));
		}

		public void AddCellEdge(WPos pos1, WPos pos2, Color color)
		{
			var anno = LineRenderFunc(pos1, pos2, color, LineEndPoint.Circle, 3);
			cellEdges.Add((new List<WPos>() { pos1, pos2 }, color, anno));

			if (Enabled)
				foreach (var a in anno)
					a.AddOrUpdateScreenMap();
			else
				foreach (var a in anno)
					a.Dispose();
		}

		public void AddOrUpdateBCDNode(BCDCellNode bcdNode)
		{
			var oldAnnos = new List<IRenderable>();
			if (bcdCellNodes[bcdNode.NodeCPos.X, bcdNode.NodeCPos.Y].Annos != null)
				oldAnnos = bcdCellNodes[bcdNode.NodeCPos.X, bcdNode.NodeCPos.Y].Annos;

			var annos = new List<IRenderable>()
			{
				TextRenderFunc(bcdNode.NodePos, bcdNode.Text, bcdNode.TextColor, bcdNode.FontName)
			};
			annos.AddRange(LineRenderFunc(bcdNode.LineStartPos, bcdNode.LineEndPos,
				bcdNode.LineColor, bcdNode.LineEnd, bcdNode.LineThickness));

			bcdCellNodes[bcdNode.NodeCPos.X, bcdNode.NodeCPos.Y] = (bcdNode, annos);

			if (Enabled)
			{
				foreach (var anno in annos)
					anno.AddOrUpdateScreenMap();

				foreach (var anno in oldAnnos)
					anno.Dispose();
			}
		}

		public void RemoveLineWithColor(List<WPos> line) { linesWithColorsAndThickness.RemoveAll(lwc => lwc.Item1 == line); }
		public void AddCircle((WPos P, WDist D) circle, int thickness = DefaultThickness)
		{
			circlesWithColors.Add((circle, Color.FromAhsv(circleHue, currSat, currLight), DefaultThickness,
			CircleRenderFunc(circle, Color.FromAhsv(circleHue, currSat, currLight), thickness)));
		}

		public void AddCircleWithColor((WPos P, WDist D) circle, Color color, int thickness = DefaultThickness)
			=> circlesWithColors.Add((circle, color, DefaultThickness, CircleRenderFunc(circle, color, thickness)));
		public void RemoveCircle((WPos P, WDist D) circle) { circlesWithColors.RemoveAll(c => c.Item1 == circle); }
		public void ClearIntervals() { statesWithColors.Clear(); }
		public void ClearPaths() { paths.Clear(); }
		public void ClearLines() { linesWithThickness.Clear(); }
		public void ClearLinesWithColors() { linesWithColorsAndThickness.Clear(); }
		public void ClearStates() { statesWithColors.Clear(); }
		public void ClearPoints() { pointsWithColors.Clear(); }
		public void ClearActorPoints() { actorPointsWithColors.Clear(); }
		public void ClearCircles() { circlesWithColors.Clear(); }
		public void ClearRadiuses() { circlesWithColors.Clear(); }
		public void ClearTexts() { textsWithColors.Clear(); }
		public void ClearActorTexts() { actorTextsWithColors.Clear(); }
		public void ClearCellEdges()
		{
			foreach (var anno in cellEdges.SelectMany(x => x.Anno).Cast<IRenderable>())
				anno.Dispose();
			cellEdges.Clear();
		}

		public void ClearBCDCellNodes()
		{
			foreach (var (_, annos) in bcdCellNodes)
				annos.Clear();
		}

		public void ClearAll()
		{
			ClearBCDCellNodes();
			ClearCellEdges();
			ClearIntervals();
			ClearPaths();
			ClearLines();
			ClearLinesWithColors();
			ClearStates();
			ClearPoints();
			ClearActorPoints();
			ClearCircles();
			ClearRadiuses();
			ClearActorTexts();
			ClearTexts();
		}
	}
}
