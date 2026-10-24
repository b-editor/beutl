using Beutl.Graphics;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Media.Geometry;

[TestFixture]
[SetCulture("en-US")]
public sealed class GeometryContextTests
{
    [Test]
    public void Edits_BuildTheRecordedPath()
    {
        AssertPath((context, _) =>
        {
            context.FillType = PathFillType.EvenOdd;
            context.MoveTo(new Point(10, 10));
            context.LineTo(new Point(60, 12));
            context.QuadraticTo(new Point(80, 30), new Point(60, 50));
            context.ConicTo(new Point(40, 70), new Point(20, 50), 0.7f);
            context.CubicTo(new Point(10, 40), new Point(0, 30), new Point(10, 20));
            context.ArcTo(new Size(15, 10), 30, true, false, new Point(40, 30));
            context.Close();
            // A segment after Close starts its contour from the last move point.
            context.LineTo(new Point(90, 90));
            context.MoveTo(new Point(5, 95));
            // A zero radius degenerates to a line, and so does an arc to the current point.
            context.ArcTo(new Size(0, 10), 0, false, true, new Point(15, 95));
            context.ArcTo(new Size(5, 5), 0, false, true, new Point(15, 95));
        },
            """
            fillType=EvenOdd
            points=18
            verbs=13
            tightBounds={Left=5,Top=10,Width=85,Height=85}
            bounds={Left=0,Top=10,Width=90,Height=85}
            svg=M10 10L60 12Q80 30 60 50Q59.512619 50.487377 59.001133 50.950417Q58.489647 51.413452 57.955315 51.851006Q57.42099 52.288559 56.865143 52.699547Q56.309292 53.110535 55.733292 53.493935Q55.157295 53.877338 54.562569 54.232216Q53.967842 54.58709 53.355865 54.91256Q52.743885 55.238029 52.116165 55.533287Q51.488441 55.828548 50.846523 56.092869Q50.204605 56.357185 49.550079 56.589912Q48.895554 56.822636 48.230042 57.023193Q47.564529 57.223747 46.889671 57.39164Q46.214813 57.559532 45.532276 57.694351Q44.849739 57.829166 44.161213 57.930573Q43.472687 58.031975 42.779865 58.09972Q42.087048 58.167465 41.391651 58.201378Q40.696255 58.235291 40 58.235291Q39.303741 58.235291 38.608345 58.201378Q37.912949 58.167461 37.220131 58.099716Q36.527313 58.031975 35.838783 57.930573Q35.150253 57.829166 34.46772 57.694351Q33.785183 57.559532 33.110325 57.39164Q32.435467 57.223747 31.769955 57.02319Q31.104439 56.822632 30.449917 56.589905Q29.795393 56.357182 29.153477 56.092865Q28.511559 55.828545 27.883835 55.533287Q27.256111 55.238029 26.644131 54.91256Q26.03215 54.58709 25.437426 54.232216Q24.842701 53.877342 24.266703 53.493938Q23.690704 53.110535 23.134855 52.699547Q22.579004 52.288559 22.044676 51.851006Q21.510349 51.413452 20.998863 50.950417Q20.487379 50.487377 20 50C10 40 0 30 10 20Q9.9743299 20.27791 9.9667587 20.561508Q9.9591885 20.845104 9.9697351 21.133707Q9.9802818 21.422308 10.008919 21.715218Q10.037558 22.008129 10.084219 22.304644Q10.13088 22.601158 10.195453 22.900562Q10.260025 23.19997 10.342352 23.501543Q10.424679 23.803116 10.524561 24.106133Q10.624445 24.409145 10.741644 24.712872Q10.858843 25.016598 10.993076 25.320305Q11.12731 25.624012 11.278254 25.92697Q11.429196 26.229923 11.596486 26.531395Q11.763776 26.83287 11.947009 27.132135Q12.130243 27.4314 12.328979 27.727737Q12.527714 28.024076 12.741473 28.316772Q12.955233 28.609467 13.1835 28.897814Q13.411768 29.186161 13.653994 29.469463Q13.896219 29.752768 14.151821 30.030348Q14.407422 30.307926 14.675783 30.579113Q14.944141 30.850296 15.224615 31.114437Q15.505087 31.378574 15.796997 31.635031Q16.088905 31.891485 16.39155 32.139641Q16.694195 32.387794 17.006844 32.627052Q17.319492 32.866306 17.641392 33.096085Q17.963291 33.325867 18.293667 33.545616Q18.624043 33.765366 18.962101 33.97456Q19.300156 34.183754 19.645079 34.381886Q19.990002 34.580017 20.340958 34.766605Q20.691914 34.953197 21.048058 35.1278Q21.404205 35.302402 21.764683 35.464596Q22.12516 35.626793 22.489103 35.776184Q22.853043 35.925575 23.219568 36.061806Q23.586094 36.198036 23.954323 36.320778Q24.322552 36.44352 24.691597 36.552479Q25.060642 36.661438 25.429617 36.756351Q25.798588 36.851261 26.166599 36.931896Q26.534609 37.012535 26.90077 37.078701Q27.266933 37.144867 27.630363 37.196407Q27.993795 37.247948 28.353621 37.284733Q28.713444 37.321518 29.068796 37.34346Q29.424149 37.365402 29.77417 37.372452Q30.124193 37.379501 30.468044 37.371639Q30.811895 37.363777 31.148746 37.341019Q31.485596 37.31826 31.814632 37.28067Q32.143669 37.243073 32.464104 37.190731Q32.784538 37.13839 33.095592 37.07143Q33.406651 37.004471 33.707581 36.923046Q34.008511 36.841621 34.298592 36.745937Q34.588669 36.650249 34.867195 36.540531Q35.145721 36.430809 35.412033 36.307323Q35.678337 36.183838 35.931786 36.046879Q36.18523 35.909916 36.425201 35.759811Q36.665176 35.609711 36.891094 35.446831Q37.117012 35.283947 37.328339 35.108681Q37.539665 34.933411 37.735889 34.746181Q37.932106 34.558949 38.112751 34.360199Q38.293396 34.161453 38.458027 33.951668Q38.622658 33.741882 38.770882 33.521572Q38.919106 33.301262 39.05056 33.07095Q39.182014 32.840637 39.296387 32.60088Q39.410759 32.361122 39.507774 32.112495Q39.60479 31.863873 39.684216 31.606983Q39.763641 31.350092 39.825279 31.085552Q39.886917 30.821012 39.930626 30.549461Q39.974331 30.277908 40 30L10 10ZM10 10L90 90M5 95L15 95L15 95
            [0] Move {X=10, Y=10}
            [1] Line {X=10, Y=10} {X=60, Y=12}
            [2] Quad {X=60, Y=12} {X=80, Y=30} {X=60, Y=50}
            [3] Conic {X=60, Y=50} {X=40, Y=70} {X=20, Y=50} w=0.7
            [4] Cubic {X=20, Y=50} {X=10, Y=40} {X=0, Y=30} {X=10, Y=20}
            [5] Conic {X=10, Y=20} {X=8.954327, Y=31.320778} {X=23.954323, Y=36.320778} w=0.70710677
            [6] Conic {X=23.954323, Y=36.320778} {X=38.954327, Y=41.320778} {X=40, Y=30} w=0.70710677
            [7] Close
            [8] Move {X=10, Y=10}
            [9] Line {X=10, Y=10} {X=90, Y=90}
            [10] Move {X=5, Y=95}
            [11] Line {X=5, Y=95} {X=15, Y=95}
            [12] Line {X=15, Y=95} {X=15, Y=95}
            [13] Done
            """);
    }

    [Test]
    public void ReadsBetweenEdits_ObserveCurrentState()
    {
        AssertPath((context, probe) =>
        {
            Observe(context, probe);
            context.FillType = PathFillType.EvenOdd;
            context.MoveTo(new Point(4, 8));
            Observe(context, probe);
            context.CubicTo(new Point(20, 0), new Point(30, 4), new Point(34, 12));
            Observe(context, probe);
            // RoundedRectGeometry places each corner arc relative to where the previous segment ended.
            context.ArcTo(new Size(6, 6), 0, false, true, new Point(4, 4) + context.LastPoint);
            Observe(context, probe);
            context.Close();
            Observe(context, probe);
            context.Clear();
            Observe(context, probe);
        },
            """
            fillType=Winding
            points=0
            verbs=0
            tightBounds={Left=0,Top=0,Width=0,Height=0}
            bounds={Left=0,Top=0,Width=0,Height=0}
            svg=
            [0] Done
            """,
            [
                "fill=Winding last=(0, 0) bounds=(0, 0, 0, 0)",
                "fill=EvenOdd last=(4, 8) bounds=(4, 8, 0, 0)",
                "fill=EvenOdd last=(34, 12) bounds=(4, 3.6393204, 30, 8.36068)",
                "fill=EvenOdd last=(38, 16) bounds=(4, 3.6393204, 34, 12.36068)",
                "fill=EvenOdd last=(38, 16) bounds=(4, 3.6393204, 34, 12.36068)",
                "fill=Winding last=(0, 0) bounds=(0, 0, 0, 0)",
            ]);
    }

    [Test]
    public void TransformBetweenEdits_ContinuesFromTheTransformedGeometry()
    {
        AssertPath((context, probe) =>
        {
            context.FillType = PathFillType.EvenOdd;
            context.MoveTo(new Point(0, 0));
            context.LineTo(new Point(10, 0));
            context.Transform(Matrix.CreateScale(2, 3) * Matrix.CreateTranslation(5, 7));
            Observe(context, probe);
            context.LineTo(new Point(10, 10));
            context.Close();
            context.Transform(Matrix.CreateRotation(0.5f));
            Observe(context, probe);
        },
            """
            fillType=EvenOdd
            points=3
            verbs=4
            tightBounds={Left=1.0319338,Top=8.540205,Width=17.551651,Height=9.5885105}
            bounds={Left=1.0319338,Top=8.540205,Width=17.551651,Height=9.5885105}
            svg=M1.0319338 8.540205L18.583584 18.128716L3.9815702 13.570081L1.0319338 8.540205Z
            [0] Move {X=1.0319338, Y=8.540205}
            [1] Line {X=1.0319338, Y=8.540205} {X=18.583584, Y=18.128716}
            [2] Line {X=18.583584, Y=18.128716} {X=3.9815702, Y=13.570081}
            [3] Close
            [4] Done
            """,
            [
                "fill=EvenOdd last=(25, 7) bounds=(5, 7, 20, 0)",
                "fill=EvenOdd last=(3.9815702, 13.570081) bounds=(1.0319338, 8.540205, 17.551651, 9.5885105)",
            ]);
    }

    [Test]
    public void EditsAfterReadingNativeObject_ContinueFromIt()
    {
        AssertPath((context, probe) =>
        {
            context.MoveTo(new Point(1, 2));
            context.LineTo(new Point(30, 2));
            probe.Observed.Add($"points={probe.NativeObject().PointCount}");
            context.LineTo(new Point(30, 40));
            context.Close();
            probe.Observed.Add($"points={probe.NativeObject().PointCount}");
        },
            """
            fillType=Winding
            points=3
            verbs=4
            tightBounds={Left=1,Top=2,Width=29,Height=38}
            bounds={Left=1,Top=2,Width=29,Height=38}
            svg=M1 2L30 2L30 40L1 2Z
            [0] Move {X=1, Y=2}
            [1] Line {X=1, Y=2} {X=30, Y=2}
            [2] Line {X=30, Y=2} {X=30, Y=40}
            [3] Close
            [4] Done
            """,
            [
                "points=2",
                "points=3",
            ]);
    }

    [Test]
    public void EditsMadeThroughNativeObject_AreKept()
    {
        AssertPath((context, probe) =>
        {
            context.MoveTo(new Point(1, 2));
            context.LineTo(new Point(30, 2));
#pragma warning disable CS0618 // A caller holding the path can still edit it through SKPath's obsolete API.
            probe.NativeObject().AddRect(SKRect.Create(40, 40, 10, 10));
#pragma warning restore CS0618
            context.LineTo(new Point(30, 40));
        },
            """
            fillType=Winding
            points=8
            verbs=9
            tightBounds={Left=1,Top=2,Width=49,Height=48}
            bounds={Left=1,Top=2,Width=49,Height=48}
            svg=M1 2L30 2M40 40L50 40L50 50L40 50L40 40ZM40 40L30 40
            [0] Move {X=1, Y=2}
            [1] Line {X=1, Y=2} {X=30, Y=2}
            [2] Move {X=40, Y=40}
            [3] Line {X=40, Y=40} {X=50, Y=40}
            [4] Line {X=50, Y=40} {X=50, Y=50}
            [5] Line {X=50, Y=50} {X=40, Y=50}
            [6] Close
            [7] Move {X=40, Y=40}
            [8] Line {X=40, Y=40} {X=30, Y=40}
            [9] Done
            """);
    }

    [Test]
    public void AddPath_AppendsWhatSKPathAddPathAppends()
    {
        using var ovalBuilder = new SKPathBuilder();
        ovalBuilder.AddOval(SKRect.Create(20, 20, 30, 10));
        using SKPath oval = ovalBuilder.Detach();

        AssertPath((context, probe) =>
        {
            context.MoveTo(new Point(0, 0));
            context.LineTo(new Point(5, 5));
            probe.AddPath(oval);
            context.LineTo(new Point(50, 50));
        },
            """
            fillType=Winding
            points=13
            verbs=10
            tightBounds={Left=0,Top=0,Width=50,Height=50}
            bounds={Left=0,Top=0,Width=50,Height=50}
            svg=M0 0L5 5M50 25Q50 25.122742 49.98193 25.245338Q49.963863 25.367933 49.927773 25.490086Q49.891678 25.612236 49.837643 25.733652Q49.783611 25.855064 49.711773 25.975449Q49.639935 26.095833 49.550461 26.214899Q49.460991 26.333963 49.354099 26.451422Q49.247208 26.568878 49.123154 26.684446Q48.9991 26.800014 48.858185 26.913414Q48.71727 27.026814 48.55983 27.137772Q48.402397 27.24873 48.228813 27.356981Q48.055229 27.465231 47.865921 27.570511Q47.676613 27.675791 47.472038 27.777847Q47.26746 27.879906 47.048107 27.978495Q46.828754 28.077082 46.595154 28.171963Q46.361553 28.266846 46.114265 28.357792Q45.866974 28.448738 45.606598 28.53553Q45.346222 28.622322 45.07338 28.704752Q44.800545 28.787182 44.5159 28.865049Q44.231255 28.942917 43.93549 29.016033Q43.639721 29.089149 43.333549 29.157343Q43.027378 29.225536 42.711536 29.288639Q42.395699 29.351742 42.07095 29.409603Q41.746201 29.467464 41.413322 29.519943Q41.080444 29.572422 40.740246 29.619392Q40.400047 29.666365 40.053345 29.707716Q39.706642 29.749069 39.354271 29.7847Q39.0019 29.82033 38.644703 29.850153Q38.287506 29.879978 37.926353 29.903923Q37.565201 29.92787 37.200954 29.945881Q36.836716 29.963892 36.470261 29.975924Q36.103802 29.987953 35.736015 29.993977Q35.368229 30 35 30Q34.631771 30 34.263988 29.993977Q33.896202 29.987953 33.529747 29.975924Q33.163292 29.963892 32.799046 29.945881Q32.434799 29.92787 32.073643 29.903923Q31.71249 29.879978 31.355297 29.850153Q30.998102 29.82033 30.645729 29.7847Q30.293354 29.749069 29.946651 29.707718Q29.599945 29.666365 29.259747 29.619392Q28.919546 29.572422 28.58667 29.519943Q28.253796 29.467464 27.929049 29.409605Q27.604298 29.351742 27.288456 29.288639Q26.972616 29.225536 26.666445 29.157343Q26.360273 29.089149 26.064508 29.016033Q25.768745 28.942917 25.4841 28.865051Q25.199455 28.787184 24.926615 28.704754Q24.653776 28.622324 24.393398 28.535532Q24.133022 28.448738 23.885735 28.357794Q23.638447 28.266846 23.404846 28.171965Q23.171242 28.077084 22.951887 27.978493Q22.732533 27.879906 22.527954 27.777847Q22.323376 27.675791 22.134069 27.570511Q21.944763 27.465231 21.771181 27.356981Q21.597599 27.24873 21.440159 27.137774Q21.282722 27.026814 21.141806 26.913414Q21.000889 26.800014 20.876837 26.684446Q20.752785 26.56888 20.645893 26.451424Q20.539001 26.333965 20.44953 26.214899Q20.360058 26.095835 20.288219 25.975451Q20.216381 25.855068 20.162354 25.733654Q20.108322 25.612238 20.072231 25.490086Q20.036139 25.367933 20.01807 25.245338Q20 25.122742 20 25Q20 24.877254 20.01807 24.75466Q20.036139 24.632065 20.072231 24.509912Q20.108322 24.38776 20.162354 24.266346Q20.216381 24.14493 20.288219 24.024546Q20.360058 23.90416 20.449532 23.785097Q20.539003 23.666033 20.645895 23.548576Q20.752785 23.431118 20.876839 23.31555Q21.000889 23.199982 21.141806 23.08658Q21.282722 22.973181 21.440161 22.862223Q21.597599 22.751265 21.771183 22.643015Q21.944763 22.534767 22.134071 22.429487Q22.32338 22.324207 22.527956 22.222149Q22.732533 22.120092 22.951887 22.021503Q23.171242 21.922916 23.404844 21.828035Q23.638447 21.733152 23.885733 21.642206Q24.133022 21.551258 24.393398 21.464466Q24.653776 21.377674 24.926617 21.295244Q25.199455 21.212814 25.484102 21.134949Q25.768745 21.057079 26.06451 20.983961Q26.360273 20.910843 26.666445 20.842651Q26.972616 20.77446 27.288456 20.711357Q27.604298 20.648254 27.929049 20.590395Q28.253796 20.532534 28.58667 20.480053Q28.919546 20.427574 29.259747 20.380602Q29.599945 20.33363 29.946651 20.292278Q30.293354 20.250929 30.645729 20.2153Q30.998102 20.179668 31.355297 20.149843Q31.71249 20.120018 32.073643 20.096073Q32.434799 20.072126 32.799046 20.054115Q33.163292 20.036106 33.529747 20.024078Q33.896202 20.012047 34.263985 20.006023Q34.631771 20 35 20Q35.368229 20 35.736015 20.006023Q36.103802 20.012047 36.470261 20.024078Q36.836716 20.036106 37.200954 20.054117Q37.565201 20.072126 37.926353 20.096073Q38.287506 20.120018 38.644703 20.149843Q39.0019 20.179668 39.354271 20.215298Q39.706642 20.250927 40.053345 20.292278Q40.400047 20.33363 40.740246 20.380602Q41.080444 20.427574 41.413322 20.480053Q41.746201 20.532534 42.07095 20.590395Q42.395699 20.648254 42.71154 20.711357Q43.027378 20.77446 43.333549 20.842651Q43.639721 20.910843 43.935486 20.983961Q44.231255 21.057079 44.5159 21.134949Q44.800545 21.212814 45.073383 21.295244Q45.346222 21.377674 45.606598 21.464466Q45.866974 21.551258 46.114265 21.642204Q46.361553 21.733152 46.595154 21.828035Q46.828754 21.922916 47.048107 22.021505Q47.26746 22.120092 47.472038 22.222149Q47.676613 22.324207 47.865921 22.429487Q48.055229 22.534767 48.228813 22.643017Q48.402397 22.751266 48.55983 22.862225Q48.71727 22.973183 48.858185 23.086582Q48.9991 23.199982 49.123154 23.31555Q49.247208 23.431118 49.354103 23.548576Q49.460991 23.666035 49.550461 23.785097Q49.639935 23.904163 49.711773 24.024548Q49.783611 24.144932 49.837643 24.266346Q49.891678 24.387762 49.927773 24.509914Q49.963863 24.632065 49.981934 24.754662Q50 24.877254 50 25ZM50 25L50 50
            [0] Move {X=0, Y=0}
            [1] Line {X=0, Y=0} {X=5, Y=5}
            [2] Move {X=50, Y=25}
            [3] Conic {X=50, Y=25} {X=50, Y=30} {X=35, Y=30} w=0.70710677
            [4] Conic {X=35, Y=30} {X=20, Y=30} {X=20, Y=25} w=0.70710677
            [5] Conic {X=20, Y=25} {X=20, Y=20} {X=35, Y=20} w=0.70710677
            [6] Conic {X=35, Y=20} {X=50, Y=20} {X=50, Y=25} w=0.70710677
            [7] Close
            [8] Move {X=50, Y=25}
            [9] Line {X=50, Y=25} {X=50, Y=50}
            [10] Done
            """);
    }

    [Test]
    public void ReadingNativeObjectAgainWithoutAnEdit_ReturnsTheSameInstance()
    {
        using var context = new GeometryContext();
        context.MoveTo(new Point(0, 0));
        context.LineTo(new Point(10, 0));

        Assert.That(context.NativeObject, Is.SameAs(context.NativeObject));
    }

    [Test]
    public void APathReadBeforeAnEdit_IsDisposedWhenEditingResumes()
    {
        SKPath first;
        SKPath second;
        using (var context = new GeometryContext())
        {
            context.MoveTo(new Point(0, 0));
            context.LineTo(new Point(10, 0));
            first = context.NativeObject;
            context.LineTo(new Point(10, 10));
            second = context.NativeObject;

            Assert.Multiple(() =>
            {
                Assert.That(second, Is.Not.SameAs(first));
                Assert.That(first.Handle, Is.EqualTo(IntPtr.Zero));
                Assert.That(second.PointCount, Is.EqualTo(3));
            });
        }

        Assert.Multiple(() =>
        {
            Assert.That(first.Handle, Is.EqualTo(IntPtr.Zero));
            Assert.That(second.Handle, Is.EqualTo(IntPtr.Zero));
        });
    }

    [Test]
    public void SettingFillTypeAfterReadingNativeObject_UpdatesThePathAlreadyReturned()
    {
        using var context = new GeometryContext();
        context.MoveTo(new Point(0, 0));
        context.LineTo(new Point(10, 0));
        SKPath first = context.NativeObject;

        context.FillType = PathFillType.EvenOdd;
        SKPath second = context.NativeObject;

        Assert.Multiple(() =>
        {
            Assert.That(first.FillType, Is.EqualTo(SKPathFillType.EvenOdd));
            Assert.That(second, Is.SameAs(first));
            Assert.That(second.FillType, Is.EqualTo(SKPathFillType.EvenOdd));
            Assert.That(second.PointCount, Is.EqualTo(2));
        });
    }

    [Test]
    public void TransformAfterReadingNativeObject_UpdatesThePathAlreadyReturned()
    {
        using var context = new GeometryContext();
        context.MoveTo(new Point(0, 0));
        context.LineTo(new Point(10, 0));
        SKPath first = context.NativeObject;

        context.Transform(Matrix.CreateTranslation(5, 7));
        SKPath second = context.NativeObject;

        Assert.Multiple(() =>
        {
            Assert.That(first.LastPoint, Is.EqualTo(new SKPoint(15, 7)));
            Assert.That(second, Is.SameAs(first));
            Assert.That(second.LastPoint, Is.EqualTo(new SKPoint(15, 7)));
        });
    }

    private static void AssertPath(
        Action<IGeometryContext, Probe> edit,
        string expectedPath,
        string[]? expectedObservations = null)
    {
        using var context = new GeometryContext();
        var probe = new Probe(() => context.NativeObject, context.AddPath, []);
        edit(context, probe);

        Assert.Multiple(() =>
        {
            Assert.That(probe.Observed, Is.EqualTo(expectedObservations ?? []));
            Assert.That(string.Join('\n', GeometryPathSnapshot.Describe(context.NativeObject)), Is.EqualTo(expectedPath));
        });
    }

    private static void Observe(IGeometryContext context, Probe probe)
    {
        Point last = context.LastPoint;
        Rect bounds = context.Bounds;
        probe.Observed.Add(
            $"fill={context.FillType} last=({last.X}, {last.Y}) bounds=({bounds.X}, {bounds.Y}, {bounds.Width}, {bounds.Height})");
    }

    private sealed record Probe(Func<SKPath> NativeObject, Action<SKPath> AddPath, List<string> Observed);

}
