using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.IO;
using System.Text;
using System.Windows.Forms;

using DeOps.Implementation;
using DeOps.Interface;
using DeOps.Components.Link;


namespace DeOps.Components.Mail
{
    internal partial class ComposeMail : ViewShell
    {
        MailControl Mail;
        LinkControl Links;

        ulong DefaultID;
        bool MessageSent;

        internal ComposeMail(MailControl mail, ulong id)
        {
            InitializeComponent();

            Mail = mail;
            Links = mail.Core.Links;
            DefaultID = id;

            ToTextBox.Text = Links.GetName(DefaultID);
        }

        internal override string GetTitle(bool small)
        {
            if (small)
                return "Compose";

            string name = Links.GetName(DefaultID);

            if (name != "")
                return "Mail " + name;

            return "Compose Mail";
        }

        internal override Size GetDefaultSize()
        {
            return new Size(450, 525);
        }

        internal override Icon GetIcon()
        {
            return MailRes.Compose;
        }

        internal override bool Fin()
        {
            if (!MessageSent && MessageBody.InputBox.Text.Length > 0)
                if (MessageBox.Show(this, "Discard Message?", "New Mail", MessageBoxButtons.YesNo) == DialogResult.No)
                    return false;

            return true;
        }

        private void LinkAdd_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            OpenFileDialog open = new OpenFileDialog();
            open.Multiselect = true;
            open.Title = "Add Files to Mail";
            open.Filter = "All files (*.*)|*.*";

            if (open.ShowDialog() == DialogResult.OK)
                foreach (string path in open.FileNames)
                {
                    bool added = false;
                    foreach (AttachedFile attached in ListFiles.Items)
                        if (attached.FilePath == path)
                            added = true;

                    if (!added)
                        ListFiles.Items.Add(new AttachedFile(path));
                }

            if (ListFiles.SelectedItem == null && ListFiles.Items.Count > 0)
                ListFiles.SelectedItem = ListFiles.Items[0];
        }

        private void LinkRemove_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {

            if (ListFiles.SelectedItem != null)
            {
                int index = ListFiles.Items.IndexOf(ListFiles.SelectedItem);

                ListFiles.Items.Remove(ListFiles.SelectedItem);

                if (ListFiles.Items.Count > 0)
                {
                    if (index < ListFiles.Items.Count)
                        ListFiles.SelectedItem = ListFiles.Items[index];
                    else if (index - 1 < ListFiles.Items.Count)
                        ListFiles.SelectedItem = ListFiles.Items[index - 1];
                }
            }
        }

        private void SendButton_Click(object sender, EventArgs e)
        {
            try
            {
                List<ulong> to = new List<ulong>();
                List<ulong> cc = new List<ulong>();

                // to
                string[] names = ToTextBox.Text.Split(new char[] { ',' });
                foreach (string name in names)
                    NametoID(name.Trim(), to);

                // cc
                names = CCTextBox.Text.Split(new char[] { ',' });
                foreach (string name in names)
                    NametoID(name.Trim(), cc);

                // files
                List<AttachedFile> files = new List<AttachedFile>();
                foreach (AttachedFile file in ListFiles.Items)
                    files.Add(file);

                // subject
                if (SubjectTextBox.Text.Length == 0)
                    throw new Exception("Subject is blank");

                // body
                if (MessageBody.InputBox.Text.Length == 0)
                    throw new Exception("Message body is blank");


                Mail.SendMail(to, cc, files, SubjectTextBox.Text, MessageBody.InputBox.Rtf);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
                return;
            }

            MessageSent = true;

            if (External != null)
                External.Close();
        }

        private void NametoID(string name, List<ulong> list)
        {
            if (name == "")
                return;

            ulong id = 0;

            lock(Links.LinkMap)
                foreach(OpLink link in Links.LinkMap.Values)
                    if(link.Name != null)
                        if (String.Compare(name, link.Name, true) == 0)
                        {
                            if (id == 0)
                            {
                                list.Add(link.DhtID);
                                return;
                            }
                            else
                                throw new Exception("There are multiple people with the name " + name);
                        }

            throw new Exception("Could not a person named " + name);
        }

        private void CancelButton_Click(object sender, EventArgs e)
        {
            if (External != null)
                External.Close();
        }

        private void BrowseTo_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            LinkChooser add = new LinkChooser(Links, 0);

            string prefix = ToTextBox.Text.Length > 0 ? ", " : "";

            if (add.ShowDialog(this) == DialogResult.OK)
            {
                if(add.People.Count > 0)
                    ToTextBox.Text += prefix;

                //foreach(ulong id in add.People)
                //    ToTextBox.Text += Links.GetName(id)
            }
        }

        private void BrowseCC_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {

        }
    }
}
