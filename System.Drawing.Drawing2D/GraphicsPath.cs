// 
// GraphicsPath.cs: stores a path
//
// Authors:
//      Miguel de Icaza (miguel@gnome.org)
//      Duncan Mak (duncan@xamarin.com)
//      Ravindra (rkumar@novell.com)
//      Sebastien Pouliot  <spouliot@xamarin.com>
//      Jordi Mas
//		Kenneth J. Pouncey ( kjpou@pt.lu )
//
//     
// Copyright 2011 Xamarin Inc.
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
#if MONOMAC
using MonoMac.CoreGraphics;
#else
using MonoTouch.CoreGraphics;
#endif

namespace System.Drawing.Drawing2D {
	internal enum CurveType { Open, Close }
	
	public sealed class GraphicsPath : ICloneable, IDisposable 
	{
		List<PointF> points;
		List<byte> types;
		FillMode fillMode;
		bool start_new_fig = true;

		internal const int CURVE_MIN_TERMS = 1;
		internal const int CURVE_MAX_TERMS = 7;
		const int FLATTEN_RECURSION_LIMIT = 10;
			
		public GraphicsPath () : this (FillMode.Alternate)
		{
		}

		public GraphicsPath (FillMode fillMode)
		{
			this.fillMode = fillMode;
			points = new List<PointF> ();
			types = new List<byte> ();
		}

		public GraphicsPath (PointF [] pts, byte [] types) : this (pts, types, FillMode.Alternate) { }
		public GraphicsPath (Point [] pts, byte [] types) : this (pts, types, FillMode.Alternate) { }

		public GraphicsPath (PointF [] pts, byte [] types, FillMode fillMode)
		{
			if (pts == null)
				throw new ArgumentNullException ("pts");
			if (types == null)
				throw new ArgumentNullException ("types");
			if (pts.Length != types.Length)
				throw new ArgumentException ("Invalid parameter passed. Number of points and types must be same.");

			this.fillMode = fillMode;
			
			foreach (int type in types){
				if (type == 0 || type == 1 || type == 3 || type == 16 || type == 32 || type == 128)
					continue;
				throw new ArgumentException ("The pts array contains an invalid value for PathPointType: " + type);
			}
			
			this.points = new List<PointF> (pts);
			this.types = new List<byte> (types);
		}

		public GraphicsPath (Point [] pts, byte [] types, FillMode fillMode)
		{
			if (pts == null)
				throw new ArgumentNullException ("pts");
			if (types == null)
				throw new ArgumentNullException ("types");
			if (pts.Length != types.Length)
				throw new ArgumentException ("Invalid parameter passed. Number of points and types must be same.");

			this.fillMode = fillMode;
			foreach (int type in types){
				if (type == 0 || type == 1 || type == 3 || type == 16 || type == 32 || type == 128)
					continue;
				throw new ArgumentException ("The pts array contains an invalid value for PathPointType: " + type);
			}
			
			this.points = new List<PointF> ();
			foreach (var point in pts)
				points.Add (new PointF (point.X, point.Y));
			
			this.types = new List<byte> (types);
		}

		void Append (float x, float y, PathPointType type, bool compress)
		{
			byte t = (byte) type;
			PointF pt = PointF.Empty;

			/* in some case we're allowed to compress identical points */
			if (compress && (points.Count > 0)) {
				/* points (X, Y) must be identical */
				PointF lastPoint = points [points.Count-1];
				if ((lastPoint.X == x) && (lastPoint.Y == y)) {
					/* types need not be identical but must handle closed subpaths */
					PathPointType last_type = (PathPointType) types [types.Count-1];
					if ((last_type & PathPointType.CloseSubpath) != PathPointType.CloseSubpath)
						return;
				}
			}

			if (start_new_fig)
				t = (byte) PathPointType.Start;
			/* if we closed a subpath, then start new figure and append */
			else if (points.Count > 0) {
				type = (PathPointType) types [types.Count-1];
				if ((type & PathPointType.CloseSubpath) != 0)
					t = (byte)PathPointType.Start;
			}

			pt.X = x;
			pt.Y = y;

			points.Add (pt);
			types.Add (t);
			start_new_fig = false;
		}

		void AppendBezier (float x1, float y1, float x2, float y2, float x3, float y3)
		{
			Append (x1, y1, PathPointType.Bezier3, false);
			Append (x2, y2, PathPointType.Bezier3, false);
			Append (x3, y3, PathPointType.Bezier3, false);
		}

		void AppendArc (bool start, float x, float y, float width, float height, float startAngle, float endAngle)
		{
			float delta, bcp;
			float sin_alpha, sin_beta, cos_alpha, cos_beta;
			
			float rx = width / 2;
			float ry = height / 2;
			
			/* center */
			float cx = x + rx;
			float cy = y + ry;
			
			/* angles in radians */        
			float alpha = (float)(startAngle * Math.PI / 180);
			float beta = (float)(endAngle * Math.PI / 180);
			
			/* adjust angles for ellipses */
			alpha = (float) Math.Atan2 (rx * Math.Sin (alpha), ry * Math.Cos (alpha));
			beta = (float) Math.Atan2 (rx * Math.Sin (beta), ry * Math.Cos (beta));
			
			if (Math.Abs (beta - alpha) > Math.PI){
				if (beta > alpha)
					beta -= (float) (2 * Math.PI);
				else
					alpha -= (float) (2 * Math.PI);
			}
			
			delta = beta - alpha;
			// http://www.stillhq.com/ctpfaq/2001/comp.text.pdf-faq-2001-04.txt (section 2.13)
			bcp = (float)(4.0 / 3 * (1 - Math.Cos (delta / 2)) / Math.Sin (delta / 2));
			
			sin_alpha = (float)Math.Sin (alpha);
			sin_beta = (float)Math.Sin (beta);
			cos_alpha = (float)Math.Cos (alpha);
			cos_beta = (float)Math.Cos (beta);
			
			// move to the starting point if we're not continuing a curve 
			if (start) {
				// starting point
				float sx = cx + rx * cos_alpha;
				float sy = cy + ry * sin_alpha;
				Append (sx, sy, PathPointType.Line, false);
			}
			
			AppendBezier (cx + rx * (cos_alpha - bcp * sin_alpha),
				      cy + ry * (sin_alpha + bcp * cos_alpha),
				      cx + rx * (cos_beta  + bcp * sin_beta),
				      cy + ry * (sin_beta  - bcp * cos_beta),
				      cx + rx *  cos_beta,
				      cy + ry *  sin_beta);
		}

		static bool NearZero (float value)
		{
			return (value >= -0.0001f) && (value <= 0.0001f);
		}
		
		void AppendArcs (float x, float y, float width, float height, float startAngle, float sweepAngle)
		{
			float drawn = 0;
			int increment;
			float endAngle;
			bool enough = false;

			if (Math.Abs (sweepAngle) >= 360) {
				AddEllipse (x, y, width, height);
				return;
			}

			endAngle = startAngle + sweepAngle;
			increment = (endAngle < startAngle) ? -90 : 90;

			// i is the number of sub-arcs drawn, each sub-arc can be at most 90 degrees.
			// there can be no more then 4 subarcs, ie. 90 + 90 + 90 + (something less than 90) 
			for (int i = 0; i < 4; i++) {
				float current = startAngle + drawn;
				float additional;

				if (enough)
					return;
                
				additional = endAngle - current; /* otherwise, add the remainder */
				if (Math.Abs (additional) > 90) {
					additional = increment;
				} else {
					// a near zero value will introduce bad artefact in the drawing (Novell #78999)
					if (NearZero (additional))
						return;

					enough = true;
				}

				/* only move to the starting pt in the 1st iteration */
				AppendArc ((i == 0),
					   x, y, width, height,      /* bounding rectangle */
					   current, current + additional);
				drawn += additional;
			}
		}

		void AppendPoint (PointF point, PathPointType type, bool compress)
		{
			Append (point.X, point.Y, type, compress);
		}
		
		void AppendCurve (PointF [] points, PointF [] tangents, int offset, int length, CurveType type)
		{
			PathPointType ptype = ((type == CurveType.Close) || (points.Length == 0)) ? PathPointType.Start : PathPointType.Line;
			int i;
			
			AppendPoint (points [offset], ptype, true);
			for (i = offset; i < offset + length; i++) {
				int j = i + 1;

				float x1 = points [i].X + tangents [i].X;
				float y1 = points [i].Y + tangents [i].Y;

				float x2 = points [j].X - tangents [j].X;
				float y2 = points [j].Y - tangents [j].Y;

				float x3 = points [j].X;
				float y3 = points [j].Y;

				AppendBezier (x1, y1, x2, y2, x3, y3);
			}

				/* complete (close) the curve using the first point */
			if (type == CurveType.Close) {
				float x1 = points [i].X + tangents [i].X;
				float y1 = points [i].Y + tangents [i].Y;

				float x2 = points [0].X - tangents [0].X;
				float y2 = points [0].Y - tangents [0].Y;

				float x3 = points [0].X;
				float y3 = points [0].Y;

				AppendBezier (x1, y1, x2, y2, x3, y3);
				CloseFigure ();
			}
		}

		public void AddClosedCurve(Point[] points)
		{

			if (points == null)
				throw new ArgumentNullException ("points");
			if (points.Length < 3)
				throw new ArgumentException ("number of points");

			AddClosedCurve (points.ToFloat (), 0.5f);
		}

		public void AddClosedCurve(PointF[] points)
		{

			if (points == null)
				throw new ArgumentNullException ("points");
			if (points.Length < 3)
				throw new ArgumentException ("number of points");

			AddClosedCurve (points, 0.5f);
		}

		public void AddClosedCurve(Point[] points, float tension)
		{
			if (points == null)
				throw new ArgumentNullException ("points");
			if (points.Length < 3)
				throw new ArgumentException ("number of points");

			AddClosedCurve (points.ToFloat (), tension);
		}

		public void AddClosedCurve(PointF[] points, float tension)
		{
			if (points == null)
				throw new ArgumentNullException ("points");
			if (points.Length < 3)
				throw new ArgumentException ("number of points");

			var tangents = GeomUtilities.GetCurveTangents (CURVE_MIN_TERMS, points, points.Length, tension, CurveType.Close);

			AppendCurve (points, tangents, 0, points.Length - 1, CurveType.Close);
		}

		public void AddCurve (Point [] points)
		{
			AddCurve (points.ToFloat(), 0.5f);
		}

		public void AddCurve (PointF [] points)
		{
			AddCurve (points, 0.5f);
		}

		public void AddCurve (Point [] points, float tension)
		{
			AddCurve (points.ToFloat (), tension);
		}

		public void AddCurve (PointF [] points, float tension)
		{
			if (points == null)
				throw new ArgumentNullException ("points");
			if (points.Length < 2)
				throw new ArgumentException ("not enough points for polygon", "points");
			
			var tangents = GeomUtilities.GetCurveTangents (CURVE_MIN_TERMS, points, points.Length, tension, CurveType.Open);
			AppendCurve (points, tangents, 0, points.Length-1, CurveType.Open);
		}
			
		public void AddCurve (Point[] points, int offset, int numberOfSegments, float tension)
		{
			AddCurve (points.ToFloat(), offset, numberOfSegments, tension);
		}

		public void AddCurve (PointF[] points, int offset, int numberOfSegments, float tension)
		{
			if (points == null)
				throw new ArgumentNullException ("points");
			if (numberOfSegments < 1)
				throw new ArgumentException ("numberOfSegments");

			int count = points.Length;
			// we need 3 points for the first curve, 2 more for each curves 
			// and it's possible to use a point prior to the offset (to calculate) 
			if (offset == 0 && numberOfSegments == 1 && count < 3)
				throw new ArgumentException ("invalid parameters");
			if (numberOfSegments >= points.Length - offset)
				throw new ArgumentException ("offset");

			var tangents = GeomUtilities.GetCurveTangents (CURVE_MIN_TERMS, points, count, tension, CurveType.Open);
			AppendCurve (points, tangents, offset, numberOfSegments, CurveType.Open);
		}
			      
		public void AddPolygon (Point [] points)
		{
			AddPolygon (points.ToFloat());
		}
		
		public void AddPolygon (PointF [] points)
		{
			if (points == null)
				throw new ArgumentNullException ("points");
			if (points.Length < 3)
				throw new ArgumentException ("not enough points for polygon", "points");
			AppendPoint (points [0], PathPointType.Start, false);
			for (int i = 1; i < points.Length; i++)
				AppendPoint (points [i], PathPointType.Line, false);

			// Add a line from the last point back to the first point if
			// they're not the same
			var last = points [points.Length-1];
			if (points [0] != last)
				AppendPoint (points [0], PathPointType.Line, false);
        
			/* close the path */
			CloseFigure ();
		}

		public void StartFigure ()
		{
			start_new_fig = true;
		}
		
		public void CloseFigure ()
		{
			if (points.Count > 0)
				types [types.Count-1] = (byte) (types [types.Count-1] | (byte) PathPointType.CloseSubpath);
			start_new_fig = true;
		}

		public void AddEllipse (RectangleF rect)
		{
			const float C1 = 0.552285f;
			float rx = rect.Width / 2;
			float ry = rect.Height / 2;
			float cx = rect.X + rx;
			float cy = rect.Y + ry;

			/* origin */
			Append (cx + rx, cy, PathPointType.Start, false);

			/* quadrant I */
			AppendBezier (cx + rx, cy - C1 * ry,
				      cx + C1 * rx, cy - ry,
				      cx, cy - ry);

			/* quadrant II */
			AppendBezier (cx - C1 * rx, cy - ry,
				      cx - rx, cy - C1 * ry,
				      cx - rx, cy);
			
			/* quadrant III */
			AppendBezier (cx - rx, cy + C1 * ry,
				      cx - C1 * rx, cy + ry,
				      cx, cy + ry);

			/* quadrant IV */
			AppendBezier (cx + C1 * rx, cy + ry,
				      cx + rx, cy + C1 * ry,
				      cx + rx, cy);

			CloseFigure ();
		}
		       
			
		public void AddEllipse (float x, float y, float width, float height)
		{
			AddEllipse (new RectangleF (x, y, width, height));
		}

		public void AddEllipse (int x, int y, int width, int height)
		{
			AddEllipse (new RectangleF (x, y, width, height));
		}

		public void AddEllipse (Rectangle rect)
		{
			AddEllipse (new RectangleF (rect.X, rect.Y, rect.Width, rect.Height));
		}

		public void AddLine (float x1, float y1, float x2, float y2)
		{
			Append (x1, y1, PathPointType.Line, true);
			Append (x2, y2, PathPointType.Line, false);
		}

		public void AddLine (int x1, int y1, int x2, int y2)
		{
			Append (x1, y1, PathPointType.Line, true);
			Append (x2, y2, PathPointType.Line, false);
		}

		public void AddLine (Point pt1, Point pt2)
		{
			Append (pt1.X, pt1.Y, PathPointType.Line, true);
			Append (pt2.X, pt2.Y, PathPointType.Line, false);
		}

		public void AddLine (PointF pt1, PointF pt2)
		{
			Append (pt1.X, pt1.Y, PathPointType.Line, true);
			Append (pt2.X, pt2.Y, PathPointType.Line, false);
		}
		
		public void AddLines (Point [] points)
		{
			if (points == null)
				throw new ArgumentNullException ("points");
			if (points.Length == 0)
				throw new ArgumentException ("points");
			
			/* only the first point can be compressed (i.e. removed if identical to previous) */
			for (int i = 0, count = points.Length; i < count; i++)
				Append (points [i].X, points [i].Y, PathPointType.Line, (i == 0));
		}
		
		public void AddLines (PointF [] points)
		{
			if (points == null)
				throw new ArgumentNullException ("points");
			if (points.Length == 0)
				throw new ArgumentException ("points");
			
			/* only the first point can be compressed (i.e. removed if identical to previous) */
			for (int i = 0, count = points.Length; i < count; i++)
				Append (points [i].X, points [i].Y, PathPointType.Line, (i == 0));
		}
		
		public void AddRectangle (Rectangle rect)
		{
			if (rect.Width == 0 || rect.Height == 0)
				return;

			Append (rect.X, rect.Y, PathPointType.Start, false);
			Append (rect.Right, rect.Y, PathPointType.Line, false);
			Append (rect.Right, rect.Bottom, PathPointType.Line, false);
			Append (rect.X, rect.Bottom, PathPointType.Line | PathPointType.CloseSubpath, false);
		}
		
		public void AddRectangle (RectangleF rect)
		{
			if (rect.Width == 0 || rect.Height == 0)
				return;

			Append (rect.X, rect.Y, PathPointType.Start, false);
			Append (rect.Right, rect.Y, PathPointType.Line, false);
			Append (rect.Right, rect.Bottom, PathPointType.Line, false);
			Append (rect.X, rect.Bottom, PathPointType.Line | PathPointType.CloseSubpath, false);
		}

		public void AddRectangles (Rectangle[] rects)
		{
			if (rects == null)
				throw new ArgumentNullException ("rects");

			foreach (var rect in rects)
				AddRectangle (rect);
		}

		public void AddRectangles (RectangleF[] rects)
		{
			if (rects == null)
				throw new ArgumentNullException ("rects");

			foreach (var rect in rects)
				AddRectangle (rect);
		}

		public void AddPie (float x, float y, float width, float height, float startAngle, float sweepAngle)
		{
			float sin_alpha, cos_alpha;

			float rx = width / 2;
			float ry = height / 2;

			/* center */
			float cx = x + rx;
			float cy = y + ry;        

			/* angles in radians */        
			float alpha = (float)(startAngle * Math.PI / 180);

			/* adjust angle for ellipses */
			alpha = (float) Math.Atan2 (rx * Math.Sin (alpha), ry * Math.Cos (alpha));

			sin_alpha = (float)Math.Sin (alpha);
			cos_alpha = (float)Math.Cos (alpha);

			/* move to center */
			Append (cx, cy, PathPointType.Start, false);

			/* draw pie edge */
			if (Math.Abs (sweepAngle) < 360)
				Append (cx + rx * cos_alpha, cy + ry * sin_alpha, PathPointType.Line, false);

			/* draw the arcs */
			AppendArcs (x, y, width, height, startAngle, sweepAngle);
        
			/* draw pie edge */
			if (Math.Abs (sweepAngle) < 360)
				Append (cx, cy, PathPointType.Line, false);

			CloseFigure ();
		}

		public void AddPie(Rectangle rect, float startAngle, float sweepAngle)
		{
			AddPie ((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height, startAngle, sweepAngle);
		}

		public void AddPie(int x, int y, int width, int height, float startAngle, float sweepAngle)
		{
			AddPie ((float)x, (float)y, (float)width, (float)height, startAngle, sweepAngle);
		}
		
        public void AddArc (Rectangle rect, float start_angle, float sweep_angle)
        {
			AppendArcs (rect.X, rect.Y, rect.Width, rect.Height, start_angle, sweep_angle);
        }

        public void AddArc (RectangleF rect, float start_angle, float sweep_angle)
        {
			AppendArcs (rect.X, rect.Y, rect.Width, rect.Height, start_angle, sweep_angle);
        }

        public void AddArc (int x, int y, int width, int height, float start_angle, float sweep_angle)
        {
			AppendArcs (x, y, width, height, start_angle, sweep_angle);
        }

        public void AddArc (float x, float y, float width, float height, float start_angle, float sweep_angle)
        {
			AppendArcs (x, y, width, height, start_angle, sweep_angle);
        }

		public void AddBezier(Point pt1, Point pt2,	Point pt3, Point pt4)
		{
			Append (pt1.X, pt1.Y, PathPointType.Line, true);
			AppendBezier (pt2.X, pt2.Y, pt3.X, pt3.Y, pt4.X, pt4.Y);
		}

		public void AddBezier(PointF pt1, PointF pt2, PointF pt3, PointF pt4)
		{
			Append (pt1.X, pt1.Y, PathPointType.Line, true);
			AppendBezier (pt2.X, pt2.Y, pt3.X, pt3.Y, pt4.X, pt4.Y);
		}

		public void AddBezier(int x1, int y1, int x2, int y2, int x3, int y3, int x4, int y4)
		{
			Append (x1, y1, PathPointType.Line, true);
			AppendBezier (x2, y2, x3, y3, x4, y4);
		}

		public void AddBezier(float x1, float y1, float x2, float y2, float x3, float y3, float x4, float y4)
		{
			Append (x1, y1, PathPointType.Line, true);
			AppendBezier (x2, y2, x3, y3, x4, y4);
		}

		public void AddBeziers(params Point[] points)
		{

			if (points == null)
				throw new ArgumentNullException ("points");

			int count = points.Length;

			/* first bezier requires 4 points, other 3 more points */
			if ((count < 4) || ((count % 3) != 1))
				throw new ArgumentException ("points");

			AddBeziers (points.ToFloat ());

		}

		public void AddBeziers(params PointF[] points)
		{

			if (points == null)
				throw new ArgumentNullException ("points");

			int count = points.Length;

			/* first bezier requires 4 points, other 3 more points */
			if ((count < 4) || ((count % 3) != 1))
				throw new ArgumentException ("points");

			AppendPoint (points [0], PathPointType.Line, true);

			for (int i = 1; i < count; i++) 
			{
				AppendPoint (points [i], PathPointType.Bezier3, false);
			}
		}

		public void AddPath(GraphicsPath addingPath, bool connect)
		{
			if (addingPath == null)
				throw new ArgumentNullException ("addingPath");

			var length = addingPath.PointCount;

			if (length < 1)
				return;

			var pts = addingPath.PathPoints;
			var types = addingPath.PathTypes;

			// We can connect only open figures. If first figure is closed
	 		// it can't be connected.
			var first = connect ? GetFirstPointType() : PathPointType.Start;

			AppendPoint (pts[0], first, false); 

			for (int i = 1; i < length; i++)
				AppendPoint (pts [i], (PathPointType)types [i], false);

		}


		public void CloseAllFigures()
		{
			int index = 0;
			byte currentType;
			byte lastType;
			byte[] oldTypes;

			/* first point is not closed */
			if (points.Count <= 1)
				return;

			oldTypes = types.ToArray();
			types = new List<byte> ();

			lastType = oldTypes[index];
			index++;

			for (index = 1; index < points.Count; index++) {
				currentType = oldTypes [index];
				/* we dont close on the first point */
				if ((currentType == (byte)PathPointType.Start) && (index > 1)) 
				{
					lastType |= (byte)PathPointType.CloseSubpath;
					types.Add (lastType);
				}
				else
					types.Add(lastType);

				lastType = currentType;
			}

			/* close at the end */
			lastType |= (byte)PathPointType.CloseSubpath;
			types.Add(lastType);

			start_new_fig = true;
		}

		public PointF GetLastPoint()
		{
			if (points.Count <= 0)
				throw new ArgumentException("Parameter is not valid");

			var lastPoint = points [points.Count - 1];
			return lastPoint;

		}

		public RectangleF GetBounds()
		{
			return GetBounds (null);
		}

		public RectangleF GetBounds(Matrix matrix)
		{
			return GetBounds (matrix, null);
		}

		// return TRUE if the specified path has (at least one) curves, FALSE otherwise */
		static bool PathHasCurve (GraphicsPath path)
		{

			if (path == null)
				return false;

			var types = path.PathTypes;
			for (int i = 0; i < types.Length; i++) {
				if (types[i] == (byte)PathPointType.Bezier)
					return true;
			}

			return false;
		}

		// nr_curve_flatten comes from Sodipodi's libnr (public domain) available from http://www.sodipodi.com/ 
		// Mono changes: converted to float (from double), added recursion limit, use List<PointF> 
		static bool	nr_curve_flatten (float x0, float y0, float x1, float y1, float x2, float y2, float x3, float y3, float flatness, int level, List<PointF> points)
		{
			float dx1_0, dy1_0, dx2_0, dy2_0, dx3_0, dy3_0, dx2_3, dy2_3, d3_0_2;
			float s1_q, t1_q, s2_q, t2_q, v2_q;
			float f2, f2_q;
			float x00t, y00t, x0tt, y0tt, xttt, yttt, x1tt, y1tt, x11t, y11t;

			dx1_0 = x1 - x0;
			dy1_0 = y1 - y0;
			dx2_0 = x2 - x0;
			dy2_0 = y2 - y0;
			dx3_0 = x3 - x0;
			dy3_0 = y3 - y0;
			dx2_3 = x3 - x2;
			dy2_3 = y3 - y2;
			f2 = flatness;
			d3_0_2 = dx3_0 * dx3_0 + dy3_0 * dy3_0;
			if (d3_0_2 < f2) {
				float d1_0_2, d2_0_2;
				d1_0_2 = dx1_0 * dx1_0 + dy1_0 * dy1_0;
				d2_0_2 = dx2_0 * dx2_0 + dy2_0 * dy2_0;
				if ((d1_0_2 < f2) && (d2_0_2 < f2)) {
					goto nosubdivide;
				} else {
					goto subdivide;
				}
			}
			f2_q = f2 * d3_0_2;
			s1_q = dx1_0 * dx3_0 + dy1_0 * dy3_0;
			t1_q = dy1_0 * dx3_0 - dx1_0 * dy3_0;
			s2_q = dx2_0 * dx3_0 + dy2_0 * dy3_0;
			t2_q = dy2_0 * dx3_0 - dx2_0 * dy3_0;
			v2_q = dx2_3 * dx3_0 + dy2_3 * dy3_0;
			if ((t1_q * t1_q) > f2_q) goto subdivide;
			if ((t2_q * t2_q) > f2_q) goto subdivide;
			if ((s1_q < 0.0) && ((s1_q * s1_q) > f2_q)) goto subdivide;
			if ((v2_q < 0.0) && ((v2_q * v2_q) > f2_q)) goto subdivide;
			if (s1_q >= s2_q) goto subdivide;

			nosubdivide:
			{
				points.Add(new PointF(x3,y3));
				return true;
			}
			subdivide:
				// things gets *VERY* memory intensive without a limit 
				if (level >= FLATTEN_RECURSION_LIMIT)
					return false;

			x00t = (x0 + x1) * 0.5f;
			y00t = (y0 + y1) * 0.5f;
			x0tt = (x0 + 2 * x1 + x2) * 0.25f;
			y0tt = (y0 + 2 * y1 + y2) * 0.25f;
			x1tt = (x1 + 2 * x2 + x3) * 0.25f;
			y1tt = (y1 + 2 * y2 + y3) * 0.25f;
			x11t = (x2 + x3) * 0.5f;
			y11t = (y2 + y3) * 0.5f;
			xttt = (x0tt + x1tt) * 0.5f;
			yttt = (y0tt + y1tt) * 0.5f;

			if (!nr_curve_flatten (x0, y0, x00t, y00t, x0tt, y0tt, xttt, yttt, flatness, level+1, points)) return false;
			if (!nr_curve_flatten (xttt, yttt, x1tt, y1tt, x11t, y11t, x3, y3, flatness, level+1, points)) return false;
			return true;
		}

		static bool ConvertBezierToLines (GraphicsPath path, int index, float flatness, List<PointF> flat_points, List<byte> flat_types)
		{
			PointF pt;
			byte type;
			int i;

			if ((index <= 0) || (index + 2 >= path.points.Count))
				return false; // bad path data 

			var start = path.points [index - 1];
			var first = path.points [index];
			var second = path.points [index + 1];
			var end = path.points [index + 2];

			// we can't add points directly to the original list as we could end up with too much recursion 
			var points = new List<PointF> ();
			if (!nr_curve_flatten (start.X, start.Y, first.X, first.Y, second.X, second.Y, end.X, end.Y, flatness, 0, points)) 
			{
				// curved path is too complex (i.e. would result in too many points) to render as a polygon 
				return false;
			}

			// recursion was within limits, append the result to the original supplied list 
			if (points.Count > 0) 
			{
				type = (byte)PathPointType.Line;
				foreach (var pts in points)
				{
					flat_points.Add (pts);
					flat_types.Add (type);
				}
			}

			// always PathPointTypeLine 
			type = (byte)PathPointType.Line;
			for (i = 1; i < points.Count; i++) 
			{

				pt = points [i];
				flat_points.Add (pt);
				flat_types.Add (type); 
			}

			return true;
		}

		static int FlattenPath (GraphicsPath path, Matrix matrix, float flatness)
		{
			var status = 0;

			if (path == null)
				return -1;

			// apply matrix before flattening (as there's less points at this stage) 
			if (matrix != null) {
				path.Transform(matrix);
			}

			// if no bezier are present then the path doesn't need to be flattened
			if (!PathHasCurve (path))
				return status;

			var points = new List<PointF> ();
			var types = new List<byte> ();

			// Iterate the current path and replace each bezier with multiple lines 
			for (int i = 0; i < path.points.Count; i++) {
				var point = path.points [i];
				var type = path.types [i];

				// PathPointTypeBezier3 has the same value as PathPointTypeBezier 
				if ((type & (byte)PathPointType.Bezier) == (byte)PathPointType.Bezier) 
				{
					if (!ConvertBezierToLines (path, i, Math.Abs (flatness), points, types)) 
					{
						// uho, too much recursion - do not pass go, do not collect 200$ 
						PointF pt = PointF.Empty;

						// mimic MS behaviour when recursion becomes a problem */
						// note: it's not really an empty rectangle as the last point isn't closing 
						points.Clear ();
						types.Clear ();

						type = (byte)PathPointType.Start;
						points.Add (pt);
						types.Add (type);

						type = (byte)PathPointType.Line;
						points.Add (pt);
						types.Add (type);

						points.Add (pt);
						types.Add (type);
						break;
					}
					// beziers have 4 points: the previous one, the current and the next two 
					i += 2;
				} else {
					// no change required, just copy the point 
					points.Add (point);
					types.Add (type);
				}
			}

			// transfer new path informations 
			path.points = points;
			path.types = types;

			// note: no error code is given for excessive recursion 
			return 0;
		}



		public RectangleF GetBounds(Matrix matrix,Pen pen)
		{

			var bounds = RectangleF.Empty;

			if (points.Count < 1) {
				return bounds;
			}

			var workPath = (GraphicsPath)Clone ();

			// We don't need a very precise flat value to get the bounds (GDI+ isn't, big time) -
			// however flattening helps by removing curves, making the rest of the algorithm a 
	        // lot simpler.
			// note: only the matrix is applied if no curves are present in the path 
			var status = FlattenPath (workPath, matrix, 25.0f);

			if (status == 0) 
			{
				int i;
				PointF boundaryPoints;

				boundaryPoints = workPath.points [0];//g_array_index (workpath->points, GpPointF, 0);
				bounds.X = boundaryPoints.X;		// keep minimum X here 
				bounds.Y = boundaryPoints.Y;		// keep minimum Y here 
				if (workPath.points.Count == 1) {
					// special case #2 - Only one element 
					bounds.Width = 0.0f;
					bounds.Height = 0.0f;
					return bounds;
				}

				bounds.Width = boundaryPoints.X;	// keep maximum X here 
				bounds.Height = boundaryPoints.Y;	// keep maximum Y here 

				for (i = 1; i < workPath.points.Count; i++) {
					boundaryPoints = workPath.points[i];
					if (boundaryPoints.X < bounds.X)
						bounds.X = boundaryPoints.X;
					if (boundaryPoints.Y < bounds.Y)
						bounds.Y = boundaryPoints.Y;
					if (boundaryPoints.X > bounds.Width)
						bounds.Width = boundaryPoints.X;
					if (boundaryPoints.Y > bounds.Height)
						bounds.Height = boundaryPoints.Y;
				}

				// convert maximum values (width/height) as length 
				bounds.Width -= bounds.X;
				bounds.Height -= bounds.Y;

				if (pen != null) 
				{
					/* in calculation the pen's width is at least 1.0 */
					float width = (pen.Width < 1.0f) ? 1.0f : pen.Width;
					float halfw = (width / 2);

					bounds.X -= halfw;
					bounds.Y -= halfw;
					bounds.Width += width;
					bounds.Height += width;
				}
			}
			return bounds;
		}

		public void Flatten()
		{
			Flatten (null, 0.25f);
		}

		public void Flatten(Matrix matrix)
		{
			Flatten (matrix, 0.25f);
		}

		public void Flatten(Matrix matrix, float flatness)
		{
			FlattenPath (this, matrix, flatness);
		}

		public void Reset()
		{
			points.Clear ();
			types.Clear ();
			fillMode = FillMode.Alternate;
			start_new_fig = true;

		}


		/*
 		 * Append old_types[start, end] to new_types, adjusting flags.
 		 */
		static void ReverseSubpathAndAdjustFlags (int start, int end, List<byte> old_types, List<byte> new_types, ref bool isPrevHadMarker)
		{

			// Copy all but PathPointTypeStart 
			if (end != start)
				new_types.AddRange (old_types.GetRange (start + 1, end - start));

			// Append PathPointTypeStart 
			new_types.Add ((byte)PathPointType.Start);

			Debug.Assert (new_types.Count == end + 1);

			var prev_first = old_types [start];    
			var prev_last = old_types [end];   

			// Remove potential flags from our future start point 
			if (end != start)
				new_types[end - 1] &= (byte)PathPointType.PathTypeMask;

			// Set the flags on our to-be-last point 
			if ((prev_last & (byte)PathPointType.DashMode) != 0)
				new_types[start] |= (byte)PathPointType.DashMode;

			if ((prev_last & (byte)PathPointType.CloseSubpath) != 0)
				new_types[start] |= (byte)PathPointType.CloseSubpath;

			//
	 		// Swap markers
	 		//
			for (int i = start + 1; i < end; i++) {
				if ((old_types [i - 1] & (byte)PathPointType.PathMarker) != 0)
					new_types [i] |= (byte)PathPointType.PathMarker;
				else
					//new_types[i] &= ~PathPointType.PathMarker;
					// Can not take compliment for negative numbers so we XOR
					new_types [i] &= ((byte)PathPointType.PathMarker ^ 0xff);
			}

			// If the last point of the previous subpath had a marker, we inherit it 
			if (isPrevHadMarker)
				new_types[start] |= (byte)PathPointType.PathMarker;
			else
				//new_types[start] &= ~PathPointType.PathMarker;
				// Can not take compliment for negative numbers so we XOR
				new_types[start] &= ((byte)PathPointType.PathMarker ^ 0xff);

			isPrevHadMarker = ((prev_last & (byte)PathPointType.PathMarker) == (byte)PathPointType.PathMarker);
		}


		public void Reverse()
		{
			var length = points.Count;
			var start = 0;
			var isPrevHadMarker = false;

			// shortcut 
			if (length <= 1)
				return;

			// PathTypes reversal 

			// First adjust the flags for each subpath 
			var newTypes = new List<byte> (length);

			for (int i = 1; i < length; i++) {
				byte t = types [i];
				if ((t & (byte)PathPointType.PathTypeMask) == (byte)PathPointType.Start) 
				{
					ReverseSubpathAndAdjustFlags (start, i - 1, types, newTypes, ref isPrevHadMarker);
					start = i;
				}
			}

			if (start < length - 1)
				ReverseSubpathAndAdjustFlags (start, length - 1, types, newTypes, ref isPrevHadMarker);

			/* Then reverse the resulting array */
			for (int i = 0; i < (length >> 1); i++) {
				byte a = newTypes [i]; 
				byte b = newTypes [length - i - 1]; 
				byte temp = a;
				newTypes [i] = b;
				newTypes [length - i - 1] = temp;
			}

			types = newTypes;			

			
			// PathPoints reversal
	 		// note: if length is odd then the middle point doesn't need to switch side
	 		//
			for (int i = 0; i < (length >> 1); i++) {
				PointF first = points [i]; 
				PointF last = points [length - i - 1]; 

				PointF temp = PointF.Empty;
				temp.X = first.X;
				temp.Y = first.Y;
				points [i] = last;
				points [length - i - 1] = temp;
			}

		}

		public void SetMarkers()
		{
			if (points.Count == 0)
				return;

			var current = types [points.Count - 1];

			types.RemoveAt (points.Count - 1);

			current |= (byte)PathPointType.PathMarker;

			types.Add(current);
		}

		public void ClearMarkers()
		{

			// shortcut to avoid allocations 
			if (types.Count == 0)
				return;

			var cleared = new List<byte> ();
			byte current = 0;

			for (int i = 0; i < types.Count; i++) {
				current = types [i];

				/* take out the marker if there is one */
				if ((current & (byte)PathPointType.PathMarker) != 0)
					//current &= ~PathPointType.PathMarker;
					current &= ((byte)PathPointType.PathMarker ^ 0xff);

				cleared.Add (current);
			}

			/* replace the existing with the cleared array */
			types = cleared;
		}

		private PathPointType GetFirstPointType()
		{
			/* check for a new figure flag or an empty path */ 
			if (start_new_fig || (points.Count == 0))
				return PathPointType.Start;

			/* check if the previous point is a closure */
			var type = types [types.Count - 1];
			if ((type & (byte)PathPointType.CloseSubpath) != 0)
				return PathPointType.Start;
			else
				return PathPointType.Line;
		}

		public void Transform (Matrix matrix)
		{
			matrix.TransformPoints (points);
		}
		
		public void Dispose ()
		{
		}

		public object Clone ()
		{
			var copy = new GraphicsPath (fillMode);
			copy.points = new List<PointF> (points);
			copy.types = new List<byte> (types);
			copy.start_new_fig = start_new_fig;

			return copy;
		}

		public PointF [] PathPoints {
			get {
				return points.ToArray ();
			}
		}

		public byte [] PathTypes {
			get {
				return types.ToArray ();
			}
		}

		public int PointCount {
			get {
				return points.Count;
			}
		}

		public PathData PathData {
			get {
				return new PathData () { Points = points.ToArray (), Types = types.ToArray () };
			}
		}

		public FillMode FillMode {
			get {
				return fillMode;
			}
			set {
				FillMode = value;
			}
		}
	}
}