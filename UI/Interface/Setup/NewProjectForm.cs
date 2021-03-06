using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

using DeOps.Implementation;


namespace DeOps.Interface
{
    public partial class NewProjectForm : CustomIconForm
    {
        OpCore Core;

        public uint ProjectID;


        public NewProjectForm(OpCore core)
            : base(core)
        {
            InitializeComponent();

            Core = core;
        }

        private void ButtonOK_Click(object sender, EventArgs e)
        {
            ProjectID = Core.Trust.CreateProject(NameBox.Text);

            DialogResult = DialogResult.OK;
            Close();
        }

        private void ButtonCancel_Click(object sender, EventArgs e)
        {
            Close();
        }
    }
}