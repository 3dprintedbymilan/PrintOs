using PrintOs.Api.Modules.Production;
using PrintOs.Api.Modules.Scheduling;

namespace PrintOs.Api.Tests;

public class SchedulePlannerTests
{
    private static readonly DateTimeOffset Now = new(2027, 3, 1, 8, 0, 0, TimeSpan.Zero);

    private static readonly Guid PrinterA = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid PrinterB = Guid.Parse("00000000-0000-0000-0000-00000000000b");

    private static SchedulePrinterInput Printer(Guid id, string name, int volume = 256, bool archived = false) => new(id, name, volume, volume, volume, archived);

    private static ScheduleJobInput Job(
        string name,
        int? minutes = 60,
        int priority = 1,
        DateOnly? due = null,
        int? orderNumber = null,
        int? position = null,
        bool pinned = false,
        Guid? printer = null,
        DateTimeOffset? pinnedStart = null,
        int? partX = null,
        int? partY = null,
        int? partZ = null,
        int created = 0,
        ScheduleFilamentNeed[]? filaments = null) =>
        new(Guid.NewGuid(), name, printer ?? PrinterA, priority, due, orderNumber, position, minutes, pinned, pinnedStart, partX, partY, partZ, Now.AddMinutes(created), filaments ?? []);

    private static SchedulePlan Plan(IReadOnlyList<ScheduleJobInput> jobs, IReadOnlyList<SchedulePrinterInput>? printers = null, IReadOnlyList<ScheduleRunningInput>? running = null, IReadOnlyList<ScheduleSpoolInput>? spools = null, IReadOnlyList<ScheduleLiveBusyInput>? live = null) =>
        SchedulePlanner.Plan(new ScheduleInput(Now, printers ?? [Printer(PrinterA, "A1 #1")], running ?? [], live ?? [], jobs, spools ?? []));

    private static ScheduleSlot SlotOf(SchedulePlan plan, ScheduleJobInput job) => plan.Slots.Single(slot => slot.JobId == job.Id);

    [Fact]
    public void Plan_QueuesJobsBackToBackOnOnePrinter()
    {
        var first = Job("first", 90, created: 0);
        var second = Job("second", 30, created: 1);

        var plan = Plan([first, second]);

        Assert.Equal((Now, Now.AddMinutes(90)), (SlotOf(plan, first).Start, SlotOf(plan, first).End));
        Assert.Equal((Now.AddMinutes(90), Now.AddMinutes(120)), (SlotOf(plan, second).Start, SlotOf(plan, second).End));
        Assert.Empty(plan.Blocked);
    }

    [Fact]
    public void Plan_OrdersByManualPositionThenPriorityThenDueDateThenOrderNumber()
    {
        var plain = Job("plain", created: 0);
        var due = Job("due", due: new DateOnly(2027, 3, 5), created: 1);
        var urgent = Job("urgent", priority: PrintJobPriority.Urgent, created: 2);
        var manual = Job("manual", position: 1, created: 3);
        var dueEarlier = Job("dueEarlier", due: new DateOnly(2027, 3, 3), created: 4);

        var plan = Plan([plain, due, urgent, manual, dueEarlier]);

        var order = plan.Slots.OrderBy(slot => slot.Start).Select(slot => new[] { plain, due, urgent, manual, dueEarlier }.Single(job => job.Id == slot.JobId).Name).ToList();
        Assert.Equal(["manual", "urgent", "dueEarlier", "due", "plain"], order);
    }

    [Fact]
    public void Plan_UsesTheLowerOrderNumberWhenDueDatesTie()
    {
        var later = Job("later", due: new DateOnly(2027, 3, 5), orderNumber: 9);
        var sooner = Job("sooner", due: new DateOnly(2027, 3, 5), orderNumber: 2);

        var plan = Plan([later, sooner]);

        Assert.True(SlotOf(plan, sooner).Start < SlotOf(plan, later).Start);
    }

    [Fact]
    public void Plan_SpreadsJobsOverPrintersByEarliestFinish()
    {
        var one = Job("one", 120, created: 0);
        var two = Job("two", 120, created: 1);
        var three = Job("three", 30, created: 2);

        var plan = Plan([one, two, three], [Printer(PrinterA, "A"), Printer(PrinterB, "B")]);

        Assert.NotEqual(SlotOf(plan, one).PrinterId, SlotOf(plan, two).PrinterId);
        Assert.Equal(Now, SlotOf(plan, two).Start);
        Assert.Equal(Now.AddMinutes(120), SlotOf(plan, three).Start);
    }

    [Fact]
    public void Plan_WaitsForJobsThatArePrinting()
    {
        var job = Job("next", 30);
        var running = new ScheduleRunningInput(Guid.NewGuid(), PrinterA, "now", Now.AddMinutes(-20), 60, []);

        var plan = Plan([job], running: [running]);

        Assert.Equal(Now.AddMinutes(40), SlotOf(plan, job).Start);
        Assert.Contains(plan.Slots, slot => slot.Running && slot.End == Now.AddMinutes(40));
    }

    [Fact]
    public void Plan_TreatsAnOverrunningPrintAsBusyRightNow()
    {
        var job = Job("next", 30);
        var running = new ScheduleRunningInput(Guid.NewGuid(), PrinterA, "late", Now.AddMinutes(-90), 60, []);

        var plan = Plan([job], running: [running]);

        Assert.Equal(Now, SlotOf(plan, job).Start);
    }

    [Fact]
    public void Plan_UsesTheLivePrinterStateWhenNoJobIsLogged()
    {
        var job = Job("next", 30);

        var plan = Plan([job], live: [new ScheduleLiveBusyInput(PrinterA, Now.AddMinutes(25))]);

        Assert.Equal(Now.AddMinutes(25), SlotOf(plan, job).Start);
    }

    [Fact]
    public void Plan_NeverUsesArchivedPrinters()
    {
        var job = Job("job");

        var plan = Plan([job], [Printer(PrinterA, "old", archived: true), Printer(PrinterB, "new")]);

        Assert.Equal(PrinterB, SlotOf(plan, job).PrinterId);
    }

    [Fact]
    public void Plan_BlocksJobsWithoutAnyUsablePrinter()
    {
        var job = Job("job");

        var plan = Plan([job], [Printer(PrinterA, "old", archived: true)]);

        Assert.Empty(plan.Slots);
        Assert.Equal(ScheduleBlockReason.NoPrinter, Assert.Single(plan.Blocked).Reason);
    }

    [Fact]
    public void Plan_RespectsTheBuildVolumeWhenThePartSizeIsKnown()
    {
        var big = Job("big", partX: 300, partY: 100, partZ: 100);
        var rotated = Job("rotated", partX: 250, partY: 200, partZ: 100);
        var unknown = Job("unknown");

        var plan = Plan([big, rotated, unknown], [Printer(PrinterA, "small", 256), Printer(PrinterB, "large", 350)]);

        Assert.Equal(PrinterB, SlotOf(plan, big).PrinterId);
        Assert.Contains(plan.Slots, slot => slot.JobId == unknown.Id);
    }

    [Fact]
    public void Plan_BlocksAPartThatFitsNoPrinter()
    {
        var job = Job("huge", partX: 500, partY: 10, partZ: 10);

        var plan = Plan([job]);

        Assert.Equal(ScheduleBlockReason.TooLarge, Assert.Single(plan.Blocked).Reason);
    }

    [Fact]
    public void Fits_IgnoresAPrinterWithoutVolume()
    {
        var job = Job("job", partX: 900, partY: 900, partZ: 900);

        Assert.True(SchedulePlanner.Fits(job, new SchedulePrinterInput(PrinterA, "unknown", 0, 0, 0, false)));
    }

    [Fact]
    public void Plan_PinnedPrinterIsRespected()
    {
        var pinned = Job("pinned", 60, pinned: true, printer: PrinterB, created: 0);
        var other = Job("other", 60, created: 1);

        var plan = Plan([pinned, other], [Printer(PrinterA, "A"), Printer(PrinterB, "B")]);

        Assert.Equal(PrinterB, SlotOf(plan, pinned).PrinterId);
        Assert.True(SlotOf(plan, pinned).Pinned);
    }

    [Fact]
    public void Plan_BlocksAPinnedJobOnAnArchivedPrinter()
    {
        var pinned = Job("pinned", pinned: true, printer: PrinterB);

        var plan = Plan([pinned], [Printer(PrinterA, "A"), Printer(PrinterB, "B", archived: true)]);

        Assert.Equal(ScheduleBlockReason.PrinterUnavailable, Assert.Single(plan.Blocked).Reason);
    }

    [Fact]
    public void Plan_PinnedStartKeepsItsSlotAndOthersFillTheGap()
    {
        var fixedJob = Job("fixed", 60, pinned: true, printer: PrinterA, pinnedStart: Now.AddHours(2));
        var shortJob = Job("short", 60, created: 1);
        var longJob = Job("long", 180, created: 2);

        var plan = Plan([fixedJob, shortJob, longJob]);

        Assert.Equal(Now.AddHours(2), SlotOf(plan, fixedJob).Start);
        Assert.Equal(Now, SlotOf(plan, shortJob).Start);
        Assert.Equal(Now.AddHours(3), SlotOf(plan, longJob).Start);
    }

    [Fact]
    public void Plan_APinnedStartInThePastStartsNow()
    {
        var job = Job("fixed", 60, pinned: true, printer: PrinterA, pinnedStart: Now.AddHours(-3));

        Assert.Equal(Now, SlotOf(Plan([job]), job).Start);
    }

    [Fact]
    public void Plan_TwoPinnedStartsThatCollideAreShifted()
    {
        var first = Job("first", 120, pinned: true, printer: PrinterA, pinnedStart: Now.AddHours(1));
        var second = Job("second", 60, pinned: true, printer: PrinterA, pinnedStart: Now.AddHours(2));

        var plan = Plan([first, second]);

        Assert.Equal(Now.AddHours(3), SlotOf(plan, second).Start);
    }

    [Fact]
    public void Plan_BlocksJobsWhenTheFilamentIsShortAndClaimsInPlanOrder()
    {
        var spool = Guid.NewGuid();
        var spools = new[] { new ScheduleSpoolInput(spool, 150, false) };
        var first = Job("first", created: 0, filaments: [new ScheduleFilamentNeed(spool, 100)]);
        var second = Job("second", created: 1, filaments: [new ScheduleFilamentNeed(spool, 100)]);
        var third = Job("third", created: 2);

        var plan = Plan([first, second, third], spools: spools);

        Assert.Contains(plan.Slots, slot => slot.JobId == first.Id);
        Assert.Contains(plan.Slots, slot => slot.JobId == third.Id);
        var blocked = Assert.Single(plan.Blocked);
        Assert.Equal((second.Id, ScheduleBlockReason.FilamentShort), (blocked.JobId, blocked.Reason));
        Assert.Equal(Now.AddMinutes(60), SlotOf(plan, third).Start);
    }

    [Fact]
    public void Plan_CountsFilamentOfRunningJobsAndRejectsArchivedSpools()
    {
        var spool = Guid.NewGuid();
        var archived = Guid.NewGuid();
        var running = new ScheduleRunningInput(Guid.NewGuid(), PrinterB, "run", Now, 60, [new ScheduleFilamentNeed(spool, 120)]);
        var needsSpool = Job("needsSpool", filaments: [new ScheduleFilamentNeed(spool, 100)]);
        var needsArchived = Job("needsArchived", filaments: [new ScheduleFilamentNeed(archived, 1)]);

        var plan = Plan([needsSpool, needsArchived], [Printer(PrinterA, "A"), Printer(PrinterB, "B")], [running], [new ScheduleSpoolInput(spool, 200, false), new ScheduleSpoolInput(archived, 500, true)]);

        Assert.Equal(2, plan.Blocked.Count);
        Assert.All(plan.Blocked, blocked => Assert.Equal(ScheduleBlockReason.FilamentShort, blocked.Reason));
    }

    [Fact]
    public void Plan_FlagsJobsThatFinishAfterTheirDueDate()
    {
        var onTime = Job("onTime", 60, due: new DateOnly(2027, 3, 1), created: 0);
        var late = Job("late", 16 * 60, due: new DateOnly(2027, 3, 1), created: 1);

        var plan = Plan([onTime, late]);

        Assert.False(SlotOf(plan, onTime).Late);
        Assert.True(SlotOf(plan, late).Late);
    }

    [Fact]
    public void Plan_FallsBackToTheDefaultDurationAndFlagsIt()
    {
        var job = Job("noEstimate", null);

        var slot = SlotOf(Plan([job]), job);

        Assert.True(slot.DurationEstimated);
        Assert.Equal(TimeSpan.FromMinutes(SchedulePlanner.DefaultDurationMinutes), slot.End - slot.Start);
    }

    [Fact]
    public void Plan_PrefersTheCurrentPrinterWhenBothAreEqual()
    {
        var job = Job("job", printer: PrinterB);

        var plan = Plan([job], [Printer(PrinterA, "A"), Printer(PrinterB, "B")]);

        Assert.Equal(PrinterB, SlotOf(plan, job).PrinterId);
    }

    [Fact]
    public void Plan_IsDeterministic()
    {
        var jobs = Enumerable.Range(0, 12).Select(index => Job($"job{index}", 30 + (index * 7), priority: index % 3, created: index % 4)).ToList();
        var printers = new[] { Printer(PrinterA, "A"), Printer(PrinterB, "B") };

        var first = Plan(jobs, printers);
        var second = Plan(jobs.AsEnumerable().Reverse().ToList(), printers);

        Assert.Equal(first.Slots, second.Slots);
    }

    [Fact]
    public void Plan_NeverOverlapsJobsOnAPrinter()
    {
        var jobs = Enumerable.Range(0, 20).Select(index => Job($"job{index}", 15 + (index * 11), created: index)).ToList();

        var plan = Plan(jobs, [Printer(PrinterA, "A"), Printer(PrinterB, "B")]);

        foreach (var lane in plan.Slots.GroupBy(slot => slot.PrinterId))
        {
            var ordered = lane.OrderBy(slot => slot.Start).ToList();
            for (var index = 1; index < ordered.Count; index++)
            {
                Assert.True(ordered[index].Start >= ordered[index - 1].End);
            }
        }
    }
}

