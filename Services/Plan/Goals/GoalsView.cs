using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Data;
using System.Text;
using System.Windows.Forms;

using DeOps.Interface;
using DeOps.Implementation;
using DeOps.Services.Trust;
using DeOps.Interface.TLVex;
using DeOps.Interface.Views;


namespace DeOps.Services.Plan
{
    internal partial class GoalsView : ViewShell
    {
        internal OpCore Core;
        internal PlanService Plans;
        internal TrustService Links;

        internal ulong DhtID;
        internal uint ProjectID;

        List<int> SpecialList = new List<int>();

        List<PlanGoal> RootList = new List<PlanGoal>();
        List<PlanGoal> ArchiveList = new List<PlanGoal>();

        internal int LoadIdent;
        internal int LoadBranch;


        StringBuilder Details = new StringBuilder(4096);
        const string DefaultPage = @"<html>
                                    <head>
                                    <style>
                                        body { font-family:tahoma; font-size:12px;margin-top:3px;}
                                        td { font-size:10px;vertical-align: middle; }
                                    </style>
                                    </head>

                                    <body bgcolor=#f5f5f5>

                                        

                                    </body>
                                    </html>";

        const string GoalPage = @"<html>
                                    <head>
                                    <style>
                                        body { font-family:tahoma; font-size:12px;margin-top:3px;}
                                        td { font-size:10px;vertical-align: middle; }
                                    </style>

                                    <script>
                                        function SetElement(id, text)
                                        {
                                            document.getElementById(id).innerHTML = text;
                                        }
                                    </script>
                                    
                                    </head>

                                    <body bgcolor=#f5f5f5>

                                        <br>
                                        <b><u><span id='title'><?=title?></span></u></b><br>
                                        <br>
                                        <b>Due</b><br>
                                        <span id='due'><?=due?></span><br>
                                        <br>
                                        <b>Assigned to</b><br>
                                        <span id='person'><?=person?></span><br>
                                        <br>
                                        <b>Notes</b><br>
                                        <span id='notes'><?=notes?></span>

                                    </body>
                                    </html>";


        const string ItemPage = @"<html>
                                    <head>
                                    <style>
                                        body { font-family:tahoma; font-size:12px;margin-top:3px;}
                                        td { font-size:10px;vertical-align: middle; }
                                    </style>

                                    <script>
                                        function SetElement(id, text)
                                        {
                                            document.getElementById(id).innerHTML = text;
                                        }
                                    </script>
                                    
                                    </head>

                                    <body bgcolor=#f5f5f5>

                                        <br>
                                        <b><u><span id='title'><?=title?></span></u></b><br>
                                        <br>
                                        <b>Progress</b><br>
                                        <span id='progress'><?=progress?></span><br>
                                        <br>
                                        <b>Notes</b><br>
                                        <span id='notes'><?=notes?></span>

                                    </body>
                                    </html>";


        internal GoalsView(PlanService plans, ulong id, uint project)
        {
            InitializeComponent();

            Plans = plans;
            Core = Plans.Core;
            Links = Core.Links;

            DhtID = id;
            ProjectID = project;

            toolStrip1.Renderer = new ToolStripProfessionalRenderer(new OpusColorTable());
            splitContainer1.Panel2Collapsed = true;

            SetDetails(null, null);
        }

        internal override string GetTitle(bool small)
        {
            if (small)
                return "Goals";

            string title = "";

            if (DhtID == Core.LocalDhtID)
                title += "My ";
            else
                title += Links.GetName(DhtID) + "'s ";

            if (ProjectID != 0)
                title += Links.GetProjectName(ProjectID) + " ";

            title += "Goals";

            return title;
        }

        internal override Size GetDefaultSize()
        {
            return new Size(475, 325);
        }

        internal override Icon GetIcon()
        {
            return PlanRes.Goals;
        }

        internal override void Init()
        {
            Links.GuiUpdate += new LinkGuiUpdateHandler(Links_Update);
            Plans.PlanUpdate += new PlanUpdateHandler(Plans_Update);

            Links.GetFocused += new LinkGetFocusedHandler(LinkandPlans_GetFocused);
            Plans.GetFocused += new PlanGetFocusedHandler(LinkandPlans_GetFocused);


            splitContainer1.Height = Height - toolStrip1.Height;

            MainPanel.Init(this);
            
         
            // research highers for assignments
            List<ulong> ids = Links.GetUplinkIDs(DhtID, ProjectID);

            foreach (ulong id in ids)
                Plans.Research(id);


            RefreshAssigned();
        }
        private void GoalsView_Load(object sender, EventArgs e)
        {
            if(LoadIdent != 0)
                foreach(PlanGoal goal in RootList)
                    if (goal.Ident == LoadIdent)
                    {
                        //crit go to specific branch ((GoalPage)tab).SelectBranch(LoadBranch);
                        // schedule needs to pass a list of the branches from the root to this node
                        // so appropriate path can be expanded

                        MainPanel.LoadGoal(goal);
                        return;
                    }

            if (RootList.Count > 0)
                MainPanel.LoadGoal(RootList[0]);
        }

        internal override bool Fin()
        {
            if (SaveLink.Visible)
            {
                DialogResult result = MessageBox.Show(this, "Save Chages to Goals?", "De-Ops", MessageBoxButtons.YesNoCancel);

                if (result == DialogResult.OK)
                    Plans.SaveLocal();
                if (result == DialogResult.Cancel)
                    return false;
            }

            Links.GuiUpdate -= new LinkGuiUpdateHandler(Links_Update);
            Plans.PlanUpdate -= new PlanUpdateHandler(Plans_Update);

            Links.GetFocused -= new LinkGetFocusedHandler(LinkandPlans_GetFocused);
            Plans.GetFocused -= new PlanGetFocusedHandler(LinkandPlans_GetFocused);

            return true;
        }

        List<ulong> LinkandPlans_GetFocused()
        {
            List<ulong> focus = new List<ulong>(); // new List<ulong>(ids);

            MainPanel.GetFocused(focus);

            return focus;
        }

        void Links_Update(ulong key)
        {
            RefreshAssigned();

            MainPanel.LinkUpdate(key);
        }

        void Plans_Update(OpPlan plan)
        {
            RefreshAssigned();

            MainPanel.PlanUpdate(plan);

            SetDetails(LastGoal, LastItem);
        }

        void RefreshAssigned()
        {
            RootList.Clear();
            ArchiveList.Clear();

            Plans.GetAssignedGoals(DhtID, ProjectID, RootList, ArchiveList);

            string label = RootList.Count.ToString();
            label += (RootList.Count == 1) ? " Goal" : " Goals";
            SelectGoalButton.Text = label;
        }

        internal void ChangesMade()
        {
            Plans_Update(Plans.LocalPlan);

            ChangesLabel.Visible = true;
            SaveLink.Visible = true;
            DiscardLink.Visible = true;

            splitContainer1.Height = Height - toolStrip1.Height - 15;
        }

        private void SaveLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            ChangesLabel.Visible = false;
            SaveLink.Visible = false;
            DiscardLink.Visible = false;

            splitContainer1.Height = Height - toolStrip1.Height;

            Plans.SaveLocal();
        }

        private void DiscardLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            ChangesLabel.Visible = false;
            SaveLink.Visible = false;
            DiscardLink.Visible = false;
            splitContainer1.Height = Height - toolStrip1.Height;

            Plans.LoadPlan(Core.LocalDhtID);
            Plans_Update(Plans.LocalPlan);
        }


        private void SelectGoal_DropDownOpening(object sender, EventArgs e)
        {
            SelectGoalButton.DropDownItems.Clear();

            // add plans
            foreach(PlanGoal goal in RootList)
                SelectGoalButton.DropDownItems.Add(new SelectMenuItem(goal, new EventHandler(SelectGoalMenu_Click)));

            // add archived if exists
            if (ArchiveList.Count > 0)
            {
                ToolStripMenuItem archived = new ToolStripMenuItem("Archived");

                foreach (PlanGoal goal in ArchiveList)
                    archived.DropDownItems.Add(new SelectMenuItem(goal, new EventHandler(SelectGoalMenu_Click)));

                SelectGoalButton.DropDownItems.Add(archived);
            }

            // if local, add create option
            if (DhtID == Core.LocalDhtID)
            {
                SelectGoalButton.DropDownItems.Add(new ToolStripSeparator());

                SelectGoalButton.DropDownItems.Add(new ToolStripMenuItem("Create Goal", null, SelectGoalMenu_Create));
            }
        }

        private void SelectGoalMenu_Click(object sender, EventArgs e)
        {
            SelectMenuItem item = sender as SelectMenuItem;

            if (item == null)
                return;

            MainPanel.LoadGoal(item.Goal);
        }


        private void SelectGoalMenu_Create(object sender, EventArgs e)
        {
            PlanGoal goal = new PlanGoal();
            goal.Ident = Core.RndGen.Next();
            goal.Project = ProjectID;
            goal.Person = Core.LocalDhtID;
            goal.End = Core.TimeNow.AddDays(30).ToUniversalTime();

            EditGoal form = new EditGoal(EditGoalMode.New, this, goal);

            if (form.ShowDialog(this) == DialogResult.OK)
            {
                ChangesMade();

                MainPanel.LoadGoal(goal);
            }
        }

        private void DetailsButton_CheckedChanged(object sender, EventArgs e)
        {
            splitContainer1.Panel2Collapsed = !DetailsButton.Checked;

        }


        /*private void ArchivedList_SelectedIndexChanged(object sender, EventArgs e)
        {
            ArchiveItem item = GetSelectedArchive();

            if (item == null)
            {
                HideLinks();
                return;
            }

            bool local = false ;

            if (item.Goal.Person == Core.LocalDhtID)
                local = true;

            ViewLink.Visible      = true;
            UnarchiveLink.Visible = local;
            EditLink.Visible      = local;
            DeleteLink.Visible    = local;

            // view or hide
            bool viewing = false;

             foreach (TabPage tab in GoalTabs.TabPages)
                if (tab.GetType() == typeof(GoalPage))
                    if (((GoalPage)tab).Goal.Ident == item.Goal.Ident)
                    {
                        viewing = true;
                        break;
                    }

            ViewLink.Text = viewing ? "Hide" : "View";
        }

        void HideLinks()
        {
            ViewLink.Visible        = false;
            UnarchiveLink.Visible   = false;
            EditLink.Visible        = false;
            DeleteLink.Visible      = false;
        }

        private void ViewLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            ArchiveItem item = GetSelectedArchive();

             bool found = false;

             foreach (TabPage tab in GoalTabs.TabPages)
                if (tab.GetType() == typeof(GoalPage))
                    if (((GoalPage)tab).Goal.Ident == item.Goal.Ident)
                    {
                        found = true;
                        SpecialList.Remove(item.Goal.Ident);
                        GoalTabs.TabPages.Remove(tab);
                        break;
                    }

            if (!found)
            {
                SpecialList.Add(item.Goal.Ident);
                AddTab(item.Goal);
            }

            ViewLink.Text = found ? "View" : "Hide";
        }

        private void UnarchiveLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            ArchiveItem item = GetSelectedArchive();

            if (item == null)
                return;

            DialogResult result = MessageBox.Show(this, "Are you sure you want to unarchive:\n" + item.Goal.Title + "?", "De-Ops", MessageBoxButtons.YesNo);

            if (result == DialogResult.No)
                return;

            item.Goal.Archived = false;

            if (SpecialList.Contains(item.Goal.Ident))
                SpecialList.Remove(item.Goal.Ident);

            ChangesMade();

            ArchivedList_SelectedIndexChanged(null, null);
        }

        private void EditLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            ArchiveItem item = GetSelectedArchive();

            if (item == null)
                return;

            EditGoal form = new EditGoal(EditGoalMode.Edit, Core, item.Goal);

            if (form.ShowDialog(this) == DialogResult.OK)
                ChangesMade();
        }

        private void DeleteLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            ArchiveItem item = GetSelectedArchive();

            if (item == null)
                return;

            DialogResult result = MessageBox.Show(this, "Are you sure you want to delete:\n" + item.Goal.Title + "?", "De-Ops", MessageBoxButtons.YesNo);

            if (result == DialogResult.No)
                return;

            Plans.LocalPlan.RemoveGoal(item.Goal);

            if (SpecialList.Contains(item.Goal.Ident))
                SpecialList.Remove(item.Goal.Ident);

            ChangesMade();

            ArchivedList_SelectedIndexChanged(null, null);
        }*/

        PlanGoal LastGoal;
        PlanItem LastItem;

        enum DetailsModeType { Uninit, None, Goal, Item };

        DetailsModeType DetailsMode;

        internal void SetDetails(PlanGoal goal, PlanItem item)
        {
            LastGoal = goal;
            LastItem = item;

            List<string[]> tuples = new List<string[]>();

            string notes = null;
            DetailsModeType mode = DetailsModeType.None;

            // get inof that needs to be set
            if (goal != null)
            {
                tuples.Add(new string[] { "title", goal.Title });
                tuples.Add(new string[] { "due", goal.End.ToLocalTime().ToString("D") });
                tuples.Add(new string[] { "person", Links.GetName(goal.Person) });
                tuples.Add(new string[] { "notes", goal.Description.Replace("\r\n", "<br>") });

                notes = goal.Description;
                mode = DetailsModeType.Goal;
            }

            else if (item != null)
            {
                tuples.Add(new string[] { "title", item.Title });
                tuples.Add(new string[] { "progress", item.HoursCompleted.ToString() + " of " + item.HoursTotal.ToString() + " Hours Completed" });
                tuples.Add(new string[] { "notes", item.Description.Replace("\r\n", "<br>") });

                notes = item.Description;
                mode = DetailsModeType.Item;
            }

            // set details button
            DetailsButton.ForeColor = Color.Black;

            if (splitContainer1.Panel2Collapsed && notes != null && notes != "")
                DetailsButton.ForeColor = Color.Red;



            if (mode != DetailsMode)
            {
                DetailsMode = mode;

                Details.Length = 0;

                if (mode == DetailsModeType.Goal)
                    Details.Append(GoalPage);
                else if (mode == DetailsModeType.Item)
                    Details.Append(ItemPage);
                else
                    Details.Append(DefaultPage);

                foreach (string[] tuple in tuples)
                    Details.Replace("<?=" + tuple[0] + "?>", tuple[1]);

                SetDisplay(Details.ToString());
            }
            else
            {
                foreach (string[] tuple in tuples)
                    DetailsBrowser.Document.InvokeScript("SetElement", new String[] { tuple[0], tuple[1] });
            }
        }

        private void SetDisplay(string html)
        {
            Debug.Assert(!html.Contains("<?"));

            // prevents clicking sound when browser navigates
            DetailsBrowser.Hide();
            DetailsBrowser.DocumentText = html;
            DetailsBrowser.Show();
        }
    }

    class SelectMenuItem : ToolStripMenuItem
    {
        internal PlanGoal Goal;

        internal SelectMenuItem(PlanGoal goal, EventHandler onClick)
            : base(goal.Title, null, onClick)
        {
            Goal = goal;
        }
    }
}