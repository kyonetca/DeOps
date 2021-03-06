﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Data;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

using DeOps.Interface;
using DeOps.Implementation.Protocol;


namespace DeOps.Simulator
{
    public partial class GenerateUsers : CustomIconForm
    {
        Random Rnd = new Random((int)DateTime.Now.Ticks);
        G2Protocol Protocol = new G2Protocol();

        public GenerateUsers()
        {
            InitializeComponent();

            NamesFolderLink.Text = Application.StartupPath + "\\Sim";
            OutputFolderLink.Text = Application.StartupPath + "\\SimNetwork";
        }

        private void NamesFolderLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            FolderBrowserDialog browser = new FolderBrowserDialog();

            browser.Description = "Select location of name files";
            browser.SelectedPath = NamesFolderLink.Text;

            if (browser.ShowDialog(this) == DialogResult.OK)
                NamesFolderLink.Text = browser.SelectedPath;
        }

        private void OutputFolderLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            FolderBrowserDialog browser = new FolderBrowserDialog();

            browser.Description = "Select location of output files";
            browser.SelectedPath = OutputFolderLink.Text;

            if (browser.ShowDialog(this) == DialogResult.OK)
                OutputFolderLink.Text = browser.SelectedPath;
        }

        private void GenerateButton_Click(object sender, EventArgs e)
        {
            GenerateButton.Enabled = false;
            GenProgress.Visible = true;

            try
            {
                if (DoGenerate())
                    Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Generation Error: " + ex.Message);
            }

            GenProgress.Visible = false;
            GenerateButton.Enabled = true;
        }

        private bool DoGenerate()
        {
            // load last / male / female names
            List<string> LastNames = ReadNames(NamesFolderLink.Text + Path.DirectorySeparatorChar + "names_last.txt");
            List<string> FirstNames = ReadNames(NamesFolderLink.Text + Path.DirectorySeparatorChar + "\\names_first.txt");
            List<string> OpNames = ReadNames(NamesFolderLink.Text + Path.DirectorySeparatorChar + "\\names_ops.txt");

            if (UsersNumeric.Value > LastNames.Count || UsersNumeric.Value > FirstNames.Count)
            {
                MessageBox.Show("Only " + FirstNames.Count.ToString() + " first and " + LastNames.Count.ToString() + " last names loaded");
                return false;
            }

            if (OrgNumeric.Value > OpNames.Count)
            {
                MessageBox.Show("Only " + OpNames.Count.ToString() + " orgs loaded");
                return false;
            }

            int users = (int) UsersNumeric.Value;
            int orgs = (int) OrgNumeric.Value;

            List<byte[]> OpKeys = new List<byte[]>();

            for (int i = 0; i < orgs; i++)
            {
                RNGCryptoServiceProvider rnd = new RNGCryptoServiceProvider();
                OpKeys.Add( Utilities.GenerateKey(rnd, 256) );
            }

            Directory.CreateDirectory(OutputFolderLink.Text);

            // choose random name combos to create profiles for
            string name = "";

            GenProgress.Value = 0;
            GenProgress.Maximum = users;

            for (int i = 0; i < users; i++)
            {
                name = FirstNames[Rnd.Next(FirstNames.Count)] + " " + LastNames[Rnd.Next(LastNames.Count)];

                // create profile
                int index = Rnd.Next(0, orgs);

                string filename = OpNames[index] + " - " + name;
                string path = OutputFolderLink.Text + Path.DirectorySeparatorChar + filename + Path.DirectorySeparatorChar + filename + ".dop";
                Directory.CreateDirectory(OutputFolderLink.Text + Path.DirectorySeparatorChar + filename);
                string password = name.Split(' ')[0].ToLower(); // lower case first name is password

                var access = SecretNetworkCheckBox.Checked ? AccessType.Secret : AccessType.Private;

                OpUser.CreateNew(path, OpNames[index], name, password, access, OpKeys[index], false);

                if (CreateGlobal.Checked)
                {
                    filename = "Global IM - " + name;
                    path = OutputFolderLink.Text + Path.DirectorySeparatorChar + filename + Path.DirectorySeparatorChar + filename + ".dop";
                    Directory.CreateDirectory(OutputFolderLink.Text + Path.DirectorySeparatorChar + filename);
                    OpUser.CreateNew(path, "Global IM", name, password, AccessType.Secret, null, true);
                }

                GenProgress.Value++;
            }

            return true;
        }

        private List<string> ReadNames(string path)
        {
            List<string> list = new List<string>();

            StreamReader file = new StreamReader(path);

            string name = file.ReadLine();

            while (name != null)
            {
                name = name.Trim();
                name = name.ToLower();
                char[] letters = name.ToCharArray();
                letters[0] = char.ToUpper(letters[0]);
                list.Add(new string(letters));

                name = file.ReadLine();
            }

            return list;
        }

        private void ExitButton_Click(object sender, EventArgs e)
        {
            Close();
        }


    }
}
